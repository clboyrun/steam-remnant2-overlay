// Google sign-in for the companion, using Google Identity Services' token model: the browser gets a short-lived
// access token for the Drive app data folder. No client secret and no server are involved.
window.googleAuth = (() => {
    let client = null;
    let pending = null;

    async function loaded() {
        for (let i = 0; i < 100 && !window.google?.accounts?.oauth2; i++)
            await new Promise(r => setTimeout(r, 100));
        if (!window.google?.accounts?.oauth2) throw new Error("Google sign-in didn't load. Check your connection.");
    }

    return {
        async init(clientId, scope) {
            await loaded();
            client = google.accounts.oauth2.initTokenClient({
                client_id: clientId,
                scope: scope,
                callback: response => {
                    const p = pending; pending = null;
                    if (!p) return;
                    if (response.error) p.reject(new Error(response.error_description || response.error));
                    else p.resolve({ accessToken: response.access_token, expiresIn: Number(response.expires_in) });
                },
                error_callback: error => {
                    const p = pending; pending = null;
                    p?.reject(new Error(error.message || error.type || "Sign-in was cancelled."));
                },
            });
        },

        // Opens Google's popup. Browsers only allow that right after a tap, so call this from a click handler.
        // prompt "" skips the consent screen when the user already agreed; hint picks their account.
        requestToken(prompt, hint) {
            if (!client) return Promise.reject(new Error("Google sign-in isn't set up."));
            pending?.reject(new Error("Replaced by a newer sign-in."));
            return new Promise((resolve, reject) => {
                pending = { resolve, reject };
                client.requestAccessToken({ prompt: prompt ?? "", login_hint: hint || undefined });
            });
        },

        revoke(token) {
            return new Promise(resolve => google.accounts.oauth2.revoke(token, () => resolve()));
        },
    };
})();

window.localCache = {
    get: key => { try { return localStorage.getItem(key); } catch { return null; } },
    set: (key, value) => { try { localStorage.setItem(key, value); } catch { } },
    remove: key => { try { localStorage.removeItem(key); } catch { } },
};
