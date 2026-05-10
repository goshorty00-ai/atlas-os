const fs = require('fs');

// Get full PostSavedAccountsAsync payload
const wpf = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Modules/Email/EmailHostView.xaml.cs', 'utf8');
const idx = wpf.indexOf('async Task PostSavedAccountsAsync');
console.log('=== PostSavedAccountsAsync FULL ===');
console.log(wpf.substring(idx, idx+2500));

// ShouldSuppressUnreadCount
console.log('\n=== ShouldSuppressUnreadCount ===');
const sIdx = wpf.indexOf('ShouldSuppressUnreadCount(');
const sImpl = wpf.indexOf('private bool ShouldSuppressUnreadCount');
console.log(wpf.substring(sImpl, sImpl+600));

// Check last log lines for unread values
console.log('\n=== LAST LOG: unread / saved-accounts ===');
const log = fs.readFileSync('C:/Users/littl/AppData/Local/AtlasOS/Email/email-debug.log', 'utf8');
const lines = log.split('\n');
const relevant = lines.filter(l => l.includes('EmailSavedAccountsPost') || l.includes('EmailApiTokenState') || l.includes('postedUnread') || l.includes('UnreadCount'));
console.log(relevant.slice(-30).join('\n'));

// Header Ee source again - check if it uses G.inboxMessages first
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');
console.log('\n=== HEADER Ee full definition ===');
const spIdx = c.indexOf('function Sp(');
const spEnd = c.indexOf('rf.createRoot(', spIdx);
const sp = c.substring(spIdx, spEnd);
// Ee definition
const eeIdx = sp.indexOf(',Ee=');
console.log(sp.substring(eeIdx, eeIdx+400));

// Selected unread chip - what variable
console.log('\n=== HEADER CHIP: selectedUnread variable ===');
const fpIdx = c.indexOf('function fp(');
const fpBody = c.substring(fpIdx, fpIdx+200);
console.log(fpBody);
// selectedUnread prop
const selUnread = c.indexOf('selectedUnread:Ee');
console.log('selectedUnread:Ee at:', selUnread);
console.log(c.substring(Math.max(0,selUnread-50), selUnread+200));

// In fp, where is selectedUnread rendered?
const fpFull = c.substring(fpIdx, fpIdx+3000);
const mRender = fpFull.indexOf('{m}');
const mRender2 = fpFull.indexOf(',children:m}');
const mRender3 = fpFull.indexOf('children:m,');
console.log('\nfp m renders at:', mRender, mRender2, mRender3);
console.log(fpFull.substring(mRender, mRender+200));
