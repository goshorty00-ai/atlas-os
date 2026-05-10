const fs = require('fs');
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

// 4. Find Ee (selected unread) in Sp
console.log('=== Sp COMPONENT: Ee (selected unread) ===');
const spIdx = c.indexOf('function Sp(');
const spEnd = c.indexOf('rf.createRoot(', spIdx);
const spBody = c.substring(spIdx, spEnd);

// Ee is selectedUnread
const eeDefIdx = spBody.indexOf('Ee=');
console.log('Ee=', spBody.substring(eeDefIdx, eeDefIdx+300));

// 5. Find select-account / selectedAccountId handler
console.log('\n=== SELECT ACCOUNT HANDLER ===');
const selAccIdx = spBody.indexOf('$(v)');  // $ is setSelectedAccountId
console.log('$(v) at:', selAccIdx, spBody.substring(selAccIdx, selAccIdx+200));

// 6. gmail-inbox-messages handler
console.log('\n=== gmail-inbox-messages HANDLER ===');
const msgIdx = c.indexOf('gmail-inbox-messages');
console.log(c.substring(msgIdx, msgIdx+600));

// 7. account-status handler: unreadCount update
console.log('\n=== account-status HANDLER: unreadCount ===');
const asIdx = c.indexOf('type==="account-status"');
console.log(c.substring(asIdx, asIdx+600));

// 8. WPF PostSavedAccountsAsync
console.log('\n=== WPF PostSavedAccountsAsync ===');
const wpf = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Modules/Email/EmailHostView.xaml.cs', 'utf8');
const psa = wpf.indexOf('PostSavedAccountsAsync');
const psaBody = wpf.indexOf('private', psa+5);
const psaFull = wpf.indexOf('PostSavedAccountsAsync', psaBody);
// Find actual implementation
const impls = [];
let pos = 0;
while(true) {
  let i = wpf.indexOf('async Task PostSavedAccountsAsync', pos);
  if(i===-1) break; impls.push(i); pos=i+1;
}
console.log('PostSavedAccountsAsync impl at:', impls);
if(impls.length > 0) console.log(wpf.substring(impls[0], impls[0]+1500));
