const fs = require('fs');

const paths = [
  'D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js',
  'D:/My Apps/AOS/Atlas.OS/bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js'
];

for (const path of paths) {
  let c = fs.readFileSync(path, 'utf8');
  let changed = false;

  // FIX 1: Remove overflow-hidden from account card so dropdown isn't clipped
  // The card uses: `relative group overflow-hidden rounded-xl border shadow-...`
  const old1 = '`relative group overflow-hidden rounded-xl border shadow-[0_0_0_1px_rgba(255,255,255,0.02)] transition-all ${H?';
  const new1 = '`relative group rounded-xl border shadow-[0_0_0_1px_rgba(255,255,255,0.02)] transition-all ${H?';
  if (c.includes(old1)) {
    c = c.replace(old1, new1);
    console.log(path + ': account card overflow-hidden removed');
    changed = true;
  } else {
    console.log(path + ': account card pattern NOT FOUND');
  }

  // FIX 2: Make dropdown z-index higher to ensure it always renders on top
  const old2 = 'className:"absolute top-12 right-3 w-48 bg-[#1a1a25] border border-white/20 rounded-lg shadow-xl z-10 overflow-hidden"';
  const new2 = 'className:"absolute top-12 right-3 w-48 bg-[#1a1a25] border border-white/20 rounded-lg shadow-xl z-50"';
  if (c.includes(old2)) {
    c = c.replace(old2, new2);
    console.log(path + ': dropdown z-index raised to z-50, overflow-hidden removed');
    changed = true;
  } else {
    console.log(path + ': dropdown pattern NOT FOUND');
  }

  // FIX 3: Improve sign-in form - clarify that email is needed and a browser opens
  // Change label from "Email Address" to "Your Gmail Address"
  const old3 = 'children:"Email Address"},{type:"email",value:b,onChange:Q=>B(Q.target.value),placeholder:"your.email@example.com"';
  const new3 = 'children:"Your Gmail Address (e.g. you@gmail.com)"},{type:"email",value:b,onChange:Q=>B(Q.target.value),placeholder:"you@gmail.com"';
  if (c.includes(old3)) {
    c = c.replace(old3, new3);
    console.log(path + ': label updated');
    changed = true;
  } else {
    // Try alternate
    const idx3 = c.indexOf('"Email Address"');
    const ctx3 = c.substring(idx3 - 20, idx3 + 200);
    console.log(path + ': Email Address label context: ' + ctx3);
  }

  // FIX 4: Change WebView2 info text to be more direct about what happens after clicking Sign In
  const old4 = 'children:"You\'ll sign in through the provider\'s official web interface in an isolated session. Your credentials are managed securely by the provider and never stored by this app."';
  const new4 = 'children:"After clicking Sign In, a secure Google sign-in window opens. Enter your Gmail password there — nothing is stored in this app."';
  if (c.includes(old4)) {
    c = c.replace(old4, new4);
    console.log(path + ': webview2 info text updated');
    changed = true;
  } else {
    console.log(path + ': webview2 info text NOT FOUND');
  }

  if (changed) {
    fs.writeFileSync(path, c);
    console.log(path + ': SAVED');
  }
}
