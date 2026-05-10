const fs = require('fs');

// 1. Accounts JSON
console.log('=== ACCOUNTS.JSON ===');
try {
  const raw = fs.readFileSync('C:/Users/littl/AppData/Local/AtlasOS/Email/accounts.json', 'utf8');
  const accounts = JSON.parse(raw);
  const safe = (Array.isArray(accounts) ? accounts : (accounts.accounts || [])).map(a => ({
    id: a.id,
    email: a.email,
    status: a.status,
    unreadCount: a.unreadCount,
    hasUnreadCount: a.hasUnreadCount,
    isPinned: a.isPinned,
    provider: a.provider,
  }));
  console.log(JSON.stringify(safe, null, 2));
} catch(e) { console.log('ERROR:', e.message); }

const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

// 2. Header "Selected unread" chip
console.log('\n=== HEADER SELECTED UNREAD CHIP ===');
const selIdx = c.indexOf('Selected unread');
console.log(c.substring(Math.max(0, selIdx-400), selIdx+200));

// 3. Find Ee / Np (Dashboard) component
console.log('\n=== Np DASHBOARD COMPONENT ===');
const npIdx = c.indexOf('function Np(');
console.log('Np at:', npIdx);
console.log(c.substring(npIdx, npIdx+3000));
