const fs = require('fs');
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

// Get the full Ee handler to see all static canned responses
const idx = c.indexOf('function vp(');
const vpBody = c.substring(idx, idx+12000);
const eeStart = vpBody.indexOf('Ee=me=>');
console.log('=== FULL Ee HANDLER (all actions) ===');
console.log(vpBody.substring(eeStart, eeStart+3000));
