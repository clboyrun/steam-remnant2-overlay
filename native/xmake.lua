-- Builds the UE4SS C++ mod against a local RE-UE4SS checkout in native/RE-UE4SS (see native/README.md).
--   xmake f -m "Game__Shipping__Win64" -y
--   xmake
includes("RE-UE4SS")
includes("Remnant2Overlay.UE4SS")
