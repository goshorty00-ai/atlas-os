const fs = require('fs');
const svc = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Modules/Email/Services/GmailApiEmailProviderService.cs', 'utf8');

// Get the full unread count implementation
console.log('=== GetUnreadCountAsync ===');
let pos = 0, impls = [];
while(true) {
  let i = svc.indexOf('GetUnreadCountAsync', pos);
  if(i===-1) break; impls.push(i); pos=i+1;
}
console.log('found at:', impls);
// Find the implementation (not interface)
const implIdx = svc.indexOf('public async Task<int> GetUnreadCountAsync');
if(implIdx !== -1) console.log(svc.substring(implIdx, implIdx+1500));
else {
  const implIdx2 = svc.indexOf('async Task<int> GetUnreadCountAsync');
  console.log(svc.substring(implIdx2, implIdx2+1500));
}

// Find resultSizeEstimate context
console.log('\n=== resultSizeEstimate context ===');
const rsIdx = svc.indexOf('resultSizeEstimate');
console.log(svc.substring(Math.max(0,rsIdx-300), rsIdx+600));

// saved-accounts handler: unreadCount default
console.log('\n=== saved-accounts: unreadCount default value in React ===');
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');
// Find the merge logic
const mergeIdx = c.indexOf('unreadCount:typeof L.unreadCount');
console.log(c.substring(mergeIdx, mergeIdx+200));
