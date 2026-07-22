// sw.js - registration only. This service worker caches NOTHING, deliberately.
//
// It exists for exactly one reason: an INSTALLED app keeps its File System Access grant on the SD
// card across launches, where an ordinary tab loses the grant when the last tab closes (see the note
// in store.js) and Chrome stops offering the prompt after three dismissals. Installability wants a
// service worker to exist, so one exists.
//
// It does not cache, because there is nothing worth caching. The app cannot do a single useful thing
// offline: every cover needs POST /v2/identify and GET /v2/art. An offline app shell is a UI that
// loads and then cannot work, bought at the price of the entire stale-asset failure class - which is
// a bad trade at any price, and was never a trade this app needed to make.
//
// Freshness is the server's job instead. SetShellCaching in Program.cs sends
// `Cache-Control: no-cache` for html/js/css, so every load revalidates against the ETag: unchanged
// files cost a 304 of a few bytes, and a deploy reaches people on their next visit with no cache
// name to bump and no version constant anyone has to remember.

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => {
    // Versions of this file up to v15 cached the app shell. Those entries are still sitting in the
    // browsers of everyone who ever loaded the old build, and nothing else will ever refresh or
    // evict them, so drop every cache this origin owns on the way in.
    event.waitUntil((async () => {
        for (const name of await caches.keys()) {
            await caches.delete(name);
        }

        await self.clients.claim();
    })());
});

// A fetch handler has historically been part of Chrome's installability criteria. This one is
// intentionally empty: not calling respondWith() leaves the request completely untouched, so it
// goes to the network exactly as it would with no service worker at all.
self.addEventListener('fetch', () => { });
