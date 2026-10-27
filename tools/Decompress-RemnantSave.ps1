# Decompress-RemnantSave.ps1
# Turns a Remnant 2 .sav into its raw (uncompressed) bytes, plus a text list of readable strings.
#
# Usage (PowerShell, from the folder this script is in):
#   powershell -ExecutionPolicy Bypass -File .\Decompress-RemnantSave.ps1 -Path "C:\path\to\profile.sav"
#
# Writes next to the input:
#   profile.sav.bin          the decompressed data (open in a hex editor such as HxD)
#   profile.sav.strings.txt  every readable text run, with its byte offset in the .bin

param([Parameter(Mandatory = $true)][string]$Path)

$data = [System.IO.File]::ReadAllBytes((Resolve-Path $Path))

# File header (12 bytes): CRC32, total uncompressed size, save version
$crc     = [BitConverter]::ToUInt32($data, 0)
$size    = [BitConverter]::ToInt32($data, 4)
$version = [BitConverter]::ToInt32($data, 8)
Write-Host ("CRC32 0x{0:X8}  size {1}  version {2}" -f $crc, $size, $version)

$out = New-Object System.IO.MemoryStream
$offset = 12
$chunk = 0
while ($offset -lt $data.Length) {
    # Chunk header (49 bytes):
    #   8  tag (C1 83 2A 9E 22 22 22 22)
    #   8  max chunk size (131072)
    #   1  compression type (3 = zlib)
    #   8  compressed size      8  uncompressed size
    #   8  compressed size      8  uncompressed size (repeated)
    $compSize   = [BitConverter]::ToInt64($data, $offset + 17)
    $uncompSize = [BitConverter]::ToInt64($data, $offset + 25)
    $start = $offset + 49

    # zlib = 2-byte header + raw deflate + 4-byte checksum. DeflateStream wants only the middle part.
    $ms = New-Object System.IO.MemoryStream($data, ($start + 2), ($compSize - 2))
    $ds = New-Object System.IO.Compression.DeflateStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
    $ds.CopyTo($out)
    $ds.Dispose()

    Write-Host ("chunk {0}: {1} -> {2} bytes" -f $chunk, $compSize, $uncompSize)
    $offset = $start + $compSize
    $chunk++
}

$bytes = $out.ToArray()
[System.IO.File]::WriteAllBytes("$Path.bin", $bytes)
Write-Host "Wrote $Path.bin ($($bytes.Length) bytes)"

# Dump readable ASCII runs of 6+ characters with their offsets
$sb = New-Object System.Text.StringBuilder
$runStart = -1
for ($i = 0; $i -le $bytes.Length; $i++) {
    $printable = ($i -lt $bytes.Length) -and ($bytes[$i] -ge 0x20) -and ($bytes[$i] -le 0x7E)
    if ($printable) { if ($runStart -lt 0) { $runStart = $i } }
    elseif ($runStart -ge 0) {
        if ($i - $runStart -ge 6) {
            $text = [System.Text.Encoding]::ASCII.GetString($bytes, $runStart, $i - $runStart)
            [void]$sb.AppendLine(("{0,8}  {1}" -f $runStart, $text))
        }
        $runStart = -1
    }
}
[System.IO.File]::WriteAllText("$Path.strings.txt", $sb.ToString())
Write-Host "Wrote $Path.strings.txt"
