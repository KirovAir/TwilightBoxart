// admin.js: the owner's panel. One stats call drives everything; while a build runs it polls.
//
// Auth is a cookie the server hands out for the configured password. The page never stores the
// password, and a 401 from stats simply means "show the login card".

const $ = (id) => document.getElementById(id);

const state = { timer: null };

async function fetchStats() {
    const res = await fetch('/v2/admin/stats');
    if (res.status === 401) return { authed: false };
    if (!res.ok) throw new Error(`${res.status} from stats`);
    return { authed: true, stats: await res.json() };
}

function show(authed) {
    $('login-panel').hidden = authed;
    $('stats-panel').hidden = !authed;
    $('stats-extra').hidden = !authed;
    $('logout').hidden = !authed;
}

const mb = (bytes) => (bytes / 1024 / 1024).toFixed(1) + ' MB';

function tile(label, value) {
    const div = document.createElement('div');
    div.className = 'admin-tile';
    const v = document.createElement('strong');
    v.textContent = value;
    const l = document.createElement('span');
    l.textContent = label;
    div.append(v, l);
    return div;
}

function render(stats) {
    const index = $('index-grid');
    index.replaceChildren(
        tile('version', stats.index.available ? stats.index.version : 'not built yet'),
        tile('games known', stats.index.rowCount.toLocaleString()),
        tile('titles resolved', stats.titles.toLocaleString()),
    );

    const build = stats.build;
    const running = build.state === 'running';
    $('rebuild').disabled = running;
    $('build-state').textContent =
        running ? 'Building… the server keeps serving meanwhile.'
            : build.state === 'failed' ? `Last build failed: ${build.error}`
                : build.state === 'succeeded' ? `Last built ${new Date(build.finishedAt).toLocaleString()} (${build.rows.toLocaleString()} rows).`
                    : '';

    const log = $('build-log');
    log.hidden = !running && build.state !== 'failed';
    log.textContent = (build.log ?? []).join('\n');

    $('instance-grid').replaceChildren(
        ...stats.caches.map(c => tile(`${c.name} cache`, `${c.files.toLocaleString()} files · ${mb(c.bytes)}`)));

    // Since boot, most recently active first; the label is whatever the client said it was.
    $('client-grid').replaceChildren(
        ...stats.clients.map(c => tile(c.client,
            `${c.requests.toLocaleString()} requests · ${c.matched.toLocaleString()}/${c.lookups.toLocaleString()} identified`)));

    $('upstream-grid').replaceChildren(
        ...stats.upstreams.map(u => tile(u.name,
            `${u.successes.toLocaleString()} hits · ${u.misses.toLocaleString()} misses · ${u.failures.toLocaleString()} errors`)));

    // Poll only while something is happening; a quiet panel costs the server nothing.
    clearTimeout(state.timer);
    if (running) state.timer = setTimeout(refresh, 2000);
}

async function refresh() {
    try {
        const { authed, stats } = await fetchStats();
        show(authed);
        if (authed) render(stats);
    } catch (e) {
        $('build-state').textContent = e.message;
    }
}

$('login-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    $('login-error').hidden = true;
    const res = await fetch('/v2/admin/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: $('password').value }),
    });
    if (res.ok) {
        $('password').value = '';
        await refresh();
        return;
    }
    const error = $('login-error');
    error.textContent = res.status === 503
        ? 'Admin is disabled on this instance: no password is configured.'
        : res.status === 429 ? 'Too many attempts; wait a minute.' : 'Wrong password.';
    error.hidden = false;
});

$('logout').addEventListener('click', async () => {
    await fetch('/v2/admin/logout', { method: 'POST' });
    show(false);
});

$('rebuild').addEventListener('click', async () => {
    const res = await fetch('/v2/admin/index/rebuild', { method: 'POST' });
    if (res.status === 401) { show(false); return; }
    await refresh();
});

refresh();
