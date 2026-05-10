const fs = require('fs');

const files = [
  'D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js',
  'D:/My Apps/AOS/Atlas.OS/bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js',
];

const replacements = [
  {
    label: 'nativeHostAllowed for Outlook',
    from: 'De=b==="webview"&&!C&&!V&&!!G&&(G==null?void 0:G.provider)==="gmail"&&(G==null?void 0:G.status)==="connected"&&!(Array.isArray(G==null?void 0:G.inboxMessages)&&(G.inboxMessages||[]).length>0),',
    to: 'De=b==="webview"&&!C&&!V&&!!G&&(((G==null?void 0:G.provider)==="gmail"&&(G==null?void 0:G.status)==="connected"&&!(Array.isArray(G==null?void 0:G.inboxMessages)&&(G.inboxMessages||[]).length>0))||((G==null?void 0:G.provider)==="outlook"&&["setup-pending","signin-required","loading","connected"].includes((G==null?void 0:G.status)||""))),',
  },
  {
    label: 'native host aria label generic',
    from: 'Native Gmail host',
    to: 'Native webmail host',
  },
  {
    label: 'native mounted fallback generic wording',
    from: 'children:"Native Gmail host not mounted."',
    to: 'children:"Native webmail host not mounted."',
  },
  {
    label: 'native mounted bounds generic wording',
    from: 'children:b||"Native Gmail host bounds unavailable."',
    to: 'children:b||"Native webmail host bounds unavailable."',
  },
  {
    label: 'outlook sign-in card insert',
    from: '):m.status==="connected"&&m.provider==="gmail"&&!Ra&&$?null:Ra&&Ra.length>0?null:o.jsx("div",{className:"absolute inset-0 flex items-center justify-center",children:o.jsxs("div",{className:"text-center max-w-md px-8",children:[o.jsx("div",{className:"w-24 h-24 rounded-2xl bg-gradient-to-br from-blue-500/20 to-purple-500/20 flex items-center justify-center mx-auto mb-6 border border-white/10",children:o.jsx(on,{className:"w-12 h-12 text-white/60"})}),o.jsx("h3",{className:"text-xl font-bold mb-3",children:"Webmail not yet connected"}),o.jsx("p",{className:"text-white/50",children:"Add or reconnect this account to load it in the secure WebView2 overlay."})]})})',
    to: '):m.status==="connected"&&m.provider==="gmail"&&!Ra&&$?null:m.provider==="outlook"&&["setup-pending","signin-required","loading"].includes(m.status)?o.jsx("div",{className:"absolute inset-0 flex items-center justify-center bg-[#0a0a0f]/55 backdrop-blur-sm z-10 pointer-events-none",children:o.jsxs("div",{className:"max-w-md text-center px-8 pointer-events-auto",children:[o.jsx("div",{className:"w-20 h-20 rounded-2xl bg-gradient-to-br from-blue-500/25 to-cyan-500/20 flex items-center justify-center mx-auto mb-5 border border-blue-400/25",children:o.jsx(on,{className:"w-10 h-10 text-blue-200"})}),o.jsx("h3",{className:"text-xl font-bold mb-3",children:"Outlook sign-in"}),o.jsx("p",{className:"text-white/55 mb-5",children:"Continue signing in to Outlook in the secure WebView."}),o.jsx("button",{onClick:()=>V==null?void 0:V(m.id),className:"px-4 py-2 bg-blue-600/80 hover:bg-blue-600 rounded-lg text-sm font-medium transition-all",children:"Open Outlook sign-in"})]})}):Ra&&Ra.length>0?null:o.jsx("div",{className:"absolute inset-0 flex items-center justify-center",children:o.jsxs("div",{className:"text-center max-w-md px-8",children:[o.jsx("div",{className:"w-24 h-24 rounded-2xl bg-gradient-to-br from-blue-500/20 to-purple-500/20 flex items-center justify-center mx-auto mb-6 border border-white/10",children:o.jsx(on,{className:"w-12 h-12 text-white/60"})}),o.jsx("h3",{className:"text-xl font-bold mb-3",children:"Webmail not yet connected"}),o.jsx("p",{className:"text-white/50",children:"Add or reconnect this account to load it in the secure WebView2 overlay."})]})})',
  },
];

for (const filePath of files) {
  let content = fs.readFileSync(filePath, 'utf8');
  for (const replacement of replacements) {
    if (!content.includes(replacement.from)) {
      console.error('Missing pattern for', replacement.label, 'in', filePath);
      process.exit(1);
    }
    content = content.replace(replacement.from, replacement.to);
  }
  fs.writeFileSync(filePath, content, 'utf8');
  console.log('Patched', filePath);
}
