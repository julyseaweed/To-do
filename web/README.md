# Browser version

Serve this directory over HTTP and open `todo.html`. It is the exact browser implementation published inside the portfolio Site. No build dependencies are required. New browser profiles start with an empty Watching list; existing browser data is retained locally.

The Windows application in `src/` remains the reference for the interface and list interactions. This browser implementation uses the same seven base/light colors, exponential position-based gradient, blank initial state, inline editing, Enter insertion, empty deletion with Undo, completion circle, link fields, column divider, and corner scaling. Card order changes within a column; shades belong to positions.

Run `node web/check-parity.mjs` from the repository root to check colors, gradient behavior, empty initialization, and the shared icon against the native source. For a Site update, copy `todo.html`, `todo.css`, `todo.js`, and `todo-icon.png` together, then verify their SHA-256 values before publishing.

Platform differences: browser code is JavaScript/CSS, native code is C#/WPF. Browser storage is localStorage; Windows storage is local Markdown. Browser glass blurs page content, not other applications. Moving/scaling/collapsing operates inside the page or iframe. Windows startup, tray integration, desktop pinning and cross-application window ordering require the native app and are absent from the browser menu. Data is not synchronized between these runtimes.
