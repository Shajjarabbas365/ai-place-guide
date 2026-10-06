const $ = (id) => document.getElementById(id);

// ---------- Map (dark, premium look) ----------
const map = L.map('map', { zoomControl: false }).setView([43.7696, 11.2558], 15); // Florence
L.control.zoom({ position: 'bottomright' }).addTo(map);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; OpenStreetMap contributors'
}).addTo(map);

// ---------- State ----------
let lang = 'en';
let cubeBox = null;
let current = null;   // { lat, lng }
let nameReq = 0;      // purani requests ko ignore karne ke liye
let guideReq = 0;

// ---------- Cube grid (lagbhag 100m x 100m, aapka apna prototype) ----------
function getCube(lat, lng) {
    const latStep = 100 / 111320;
    const lngStep = 100 / (111320 * Math.cos(lat * Math.PI / 180));
    const row = Math.floor(lat / latStep);
    const col = Math.floor(lng / lngStep);
    return {
        id: `${row}:${col}`,
        bounds: [[row * latStep, col * lngStep], [(row + 1) * latStep, (col + 1) * lngStep]]
    };
}

async function getJson(url) {
    const res = await fetch(url);
    let data = null;
    try { data = await res.json(); } catch { /* server ne JSON nahi diya */ }
    return { ok: res.ok, data };
}

// ---------- Jagah chunna ----------
function selectPlace(lat, lng) {
    current = { lat, lng };
    const cube = getCube(lat, lng);

    if (cubeBox) cubeBox.remove();
    cubeBox = L.rectangle(cube.bounds, {
        color: '#7c9cff', weight: 2, fillColor: '#7c9cff', fillOpacity: 0.18
    }).addTo(map);

    $('empty').hidden = true;
    $('content').hidden = false;
    $('placeName').textContent = 'Locating…';
    $('placeSub').textContent = '';
    $('cubeId').textContent = cube.id;
    $('coords').textContent = `${lat.toFixed(4)}, ${lng.toFixed(4)}`;

    loadName();
    loadGuide();
}

async function loadName() {
    const id = ++nameReq;
    const { lat, lng } = current;
    const { ok, data } = await getJson(`/api/reverse?lat=${lat.toFixed(6)}&lng=${lng.toFixed(6)}`);
    if (id !== nameReq) return;
    if (!ok || !data) { $('placeName').textContent = 'Unnamed place'; return; }
    $('placeName').textContent = data.title;
    $('placeSub').textContent = [data.city, data.country].filter(Boolean).join(', ');
}

async function loadGuide() {
    if (!current) return;
    const id = ++guideReq;
    stopSpeech();
    $('guideSkeleton').hidden = false;
    $('guideText').hidden = true;
    $('guideError').hidden = true;
    $('places').innerHTML = '';
    $('listen').disabled = true;

    const { lat, lng } = current;
    const { ok, data } = await getJson(`/api/guide?lat=${lat.toFixed(6)}&lng=${lng.toFixed(6)}&lang=${lang}`);
    if (id !== guideReq) return;

    $('guideSkeleton').hidden = true;

    if (!ok || !data || !data.guide) {
        const err = $('guideError');
        err.textContent = (data && (data.message || data.detail)) || 'Could not load the guide. Please try again.';
        err.hidden = false;
        return;
    }

    $('guideText').textContent = data.guide;
    $('guideText').hidden = false;
    $('listen').disabled = false;

    for (const title of data.places) {
        const a = document.createElement('a');
        a.href = 'https://en.wikipedia.org/wiki/' + encodeURIComponent(title.replace(/ /g, '_'));
        a.target = '_blank';
        a.rel = 'noopener';
        const chip = document.createElement('span');
        chip.className = 'chip soft';
        chip.textContent = title;
        a.appendChild(chip);
        $('places').appendChild(a);
    }
}

// ---------- Map click ----------
map.on('click', (e) => {
    hideResults();
    selectPlace(e.latlng.lat, e.latlng.lng);
});

// ---------- Search ----------
function hideResults() { $('results').hidden = true; }

async function doSearch() {
    const q = $('q').value.trim();
    if (!q) return;

    const box = $('results');
    box.innerHTML = '<div class="note">Searching…</div>';
    box.hidden = false;

    const { ok, data } = await getJson('/api/search?q=' + encodeURIComponent(q));
    box.innerHTML = '';

    if (!ok || !Array.isArray(data) || data.length === 0) {
        box.innerHTML = '<div class="note">No places found. Try another name.</div>';
        return;
    }

    for (const r of data) {
        const parts = r.name.split(',');
        const btn = document.createElement('button');
        btn.textContent = parts[0].trim();
        const rest = document.createElement('span');
        rest.textContent = parts.slice(1).join(',').trim();
        btn.appendChild(rest);
        btn.addEventListener('click', () => {
            hideResults();
            map.flyTo([r.lat, r.lng], 15, { duration: 1.2 });
            selectPlace(r.lat, r.lng);
        });
        box.appendChild(btn);
    }
}

$('searchBtn').addEventListener('click', doSearch);
$('q').addEventListener('keydown', (e) => { if (e.key === 'Enter') doSearch(); });

// ---------- Meri location ----------
$('locate').addEventListener('click', () => {
    if (!navigator.geolocation) { alert('Location is not supported in this browser.'); return; }
    navigator.geolocation.getCurrentPosition(
        (pos) => {
            const { latitude, longitude } = pos.coords;
            map.flyTo([latitude, longitude], 16, { duration: 1.2 });
            selectPlace(latitude, longitude);
        },
        () => alert('Could not get your location. Please allow location access in the browser.')
    );
});

// ---------- Language toggle ----------
function setLang(l) {
    if (lang === l) return;
    lang = l;
    $('langEn').classList.toggle('active', l === 'en');
    $('langIt').classList.toggle('active', l === 'it');
    loadGuide();   // usi jagah ki guide nayi zubaan mein
}
$('langEn').addEventListener('click', () => setLang('en'));
$('langIt').addEventListener('click', () => setLang('it'));

// ---------- Listen (text-to-speech) ----------
function stopSpeech() {
    speechSynthesis.cancel();
    $('listenText').textContent = 'Listen';
}

$('listen').addEventListener('click', () => {
    if (speechSynthesis.speaking) { stopSpeech(); return; }
    const speech = new SpeechSynthesisUtterance($('guideText').textContent);
    speech.lang = lang === 'it' ? 'it-IT' : 'en-US';
    speech.onend = () => { $('listenText').textContent = 'Listen'; };
    $('listenText').textContent = 'Stop';
    speechSynthesis.speak(speech);
});