const fs = require('fs');
const paths = [
  'D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js',
  'D:/My Apps/AOS/Atlas.OS/bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js'
];

// Replace the end of the z handler to show a visible toast overlay
const oldEnd = `X(pt.join(" \u2022 "));}`;
const newEnd = `(function(msg){var t=document.getElementById("atlas-digest-toast");if(!t){t=document.createElement("div");t.id="atlas-digest-toast";t.style.cssText="position:fixed;bottom:24px;left:50%;transform:translateX(-50%);background:linear-gradient(135deg,#1e1b4b,#2d1b69);border:1px solid rgba(139,92,246,0.4);color:#fff;padding:14px 22px;border-radius:16px;font-size:13px;font-family:inherit;z-index:9999;box-shadow:0 8px 32px rgba(0,0,0,0.5);max-width:600px;line-height:1.5;white-space:pre-wrap;text-align:center;";document.body.appendChild(t);}t.textContent=msg;t.style.opacity="1";if(t._timeout)clearTimeout(t._timeout);t._timeout=setTimeout(function(){t.style.transition="opacity 0.5s";t.style.opacity="0";},8000);})(pt.join("\\n"));}`;

for (const path of paths) {
  let c = fs.readFileSync(path, 'utf8');
  if (c.includes(oldEnd)) {
    c = c.replace(oldEnd, newEnd);
    fs.writeFileSync(path, c);
    console.log(path + ': AI Digest toast added');
  } else {
    console.log(path + ': pattern not found, checking...');
    const idx = c.indexOf('pt.join(" \u2022 "))');
    console.log('  pt.join at:', idx, c.substring(Math.max(0,idx-50), idx+100));
  }
}
