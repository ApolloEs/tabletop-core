// Bootstrap: build the core (store + connection), hand it to the renderer,
// and wire the two browser-lifecycle concerns that need the DOM.

import { createStore } from "./core/store.js";
import { createGameConnection } from "./core/connection.js";
import { initRenderer } from "./render.js";

const store = createStore();
const connection = createGameConnection({
  signalR: window.signalR,      // the vendored UMD bundle (js/lib/signalr.min.js)
  store,
  storage: safeLocalStorage(),
});

initRenderer(store, connection);

// On iOS Safari, locking the screen kills the connection as a matter of
// course — waking the page reconnects proactively instead of waiting for
// the next tap to fail.
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible")
    connection.ensureConnected().catch(() => {});
});

connection.start().catch(() =>
  store.update({ connection: "lost", lastError: { code: "offline", message: "Can't reach the server — is it running?" } }));

function safeLocalStorage() {
  try {
    window.localStorage.getItem("probe");
    return window.localStorage;
  } catch {
    return null; // private mode etc. — rejoining by code still works
  }
}
