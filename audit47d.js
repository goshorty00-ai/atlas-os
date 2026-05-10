const fs = require('fs');
const c = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

// Find where G (selectedAccount) is defined in Sp
const spIdx = c.indexOf('function Sp(');
const spEnd = c.indexOf('rf.createRoot(', spIdx);
const sp = c.substring(spIdx, spEnd);

// G = m.find(v=>v.id===d)  where m=accounts, d=selectedAccountId
const gDef = sp.indexOf(',G=m.find');
console.log('G defined as:', sp.substring(gDef, gDef+100));

// saved-accounts handler: does it update unreadCount properly?
console.log('\n=== saved-accounts HANDLER ===');
const saIdx = c.indexOf('saved-accounts"');
console.log(c.substring(saIdx, saIdx+800));

// gmail-api-unread-count handler in WPF
const wpf = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Modules/Email/EmailHostView.xaml.cs', 'utf8');
console.log('\n=== WPF HandleGmailApiUnreadCountAsync ===');
const ucIdx = wpf.indexOf('HandleGmailApiUnreadCountAsync');
const ucImpl = wpf.lastIndexOf('HandleGmailApiUnreadCountAsync');
// find the actual implementation
let pos = 0, impls = [];
while(true) {
  let i = wpf.indexOf('async Task HandleGmailApiUnreadCountAsync', pos);
  if(i===-1) break; impls.push(i); pos=i+1;
}
if(impls.length>0) console.log(wpf.substring(impls[0], impls[0]+600));

// What does GetInboxUnreadCountAsync return? Is 201 hardcoded?
console.log('\n=== GmailApiEmailProviderService.cs - unread ===');
const svc = fs.readFileSync('D:/My Apps/AOS/Atlas.OS/Modules/Email/Services/GmailApiEmailProviderService.cs', 'utf8');
const unreadIdx = svc.indexOf('unread');
const unread201 = svc.indexOf('201');
const countIdx = svc.indexOf('UnreadCount');
console.log('UnreadCount at:', countIdx, svc.substring(countIdx, countIdx+300));
console.log('201 at:', unread201, unread201!==-1 ? svc.substring(Math.max(0,unread201-100), unread201+100) : 'NOT FOUND');

// What is the actual unread count source?
const getUnread = svc.indexOf('GetInboxUnreadCount');
console.log('\nGetInboxUnreadCount:', getUnread, getUnread!==-1 ? svc.substring(getUnread, getUnread+400) : 'NOT FOUND');
const resultCount = svc.indexOf('resultSizeEstimate');
console.log('resultSizeEstimate:', resultCount, resultCount!==-1 ? svc.substring(Math.max(0,resultCount-100), resultCount+200) : 'NOT FOUND');
