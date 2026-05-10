const fs = require('fs');

const paths = [
  'D:/My Apps/AOS/Atlas.OS/Figma/Email/dist/assets/index-C4iwRHDc.js',
  'D:/My Apps/AOS/Atlas.OS/bin/x64/Figma/Email/dist/assets/index-C4iwRHDc.js'
];

const oldZ = `z=()=>{if(!G||Me.length===0){X("Load API messages first.");return}const v=["urgent","asap","invoice","payment","password","verify","security","wire","gift card"];let w=0;for(const P of Me){const I=(typeof P=="string"?P:JSON.stringify(P??"")).toLowerCase();v.some(D=>I.includes(D))&&(w+=1)}X(w>0?\`Rules Scan: \${w} of \${Me.length} messages matched local risk rules.\`:\`Rules Scan: no local rule matches across \${Me.length} messages.\`)}`;

const newZ = `z=()=>{if(!G){X("Select an account first.");return}const v=Me.length>0?Me:(G.recentMessages||[]);if(v.length===0){X("AI Digest: No messages loaded — try refreshing.");return}const sc=["security","alert","verify","suspicious","sign-in","2fa","authentication"],ur=["urgent","asap","deadline","immediately","action required","expires"],pr=["unsubscribe","promotion","deal","offer","discount","sale","newsletter"];let se=0,un=0,pm=0,sn={};for(const P of v){const I=(typeof P==="string"?P:JSON.stringify(P??"")).toLowerCase();const fr=typeof P==="object"&&P!==null?(P.from||P.sender||P.fromEmail||""):"";if(fr){const k=(fr.split("@")[0]||fr).substring(0,20);sn[k]=(sn[k]||0)+1;}if(sc.some(D=>I.includes(D)))se++;if(ur.some(D=>I.includes(D)))un++;if(pr.some(D=>I.includes(D)))pm++;}const ts=Object.entries(sn).sort((a,b)=>b[1]-a[1])[0];const pt=["AI Digest: "+v.length+" messages"];if(se>0)pt.push(se+" security");if(un>0)pt.push(un+" action-needed");if(pm>0)pt.push(pm+" promo");if(ts)pt.push("top: "+ts[0]+" ("+ts[1]+")");X(pt.join(" \u2022 "));}`;

for (const path of paths) {
  let c = fs.readFileSync(path, 'utf8');
  let changed = false;

  // 1. Enable button when account selected
  if (c.includes('Te=Me.length>0,')) {
    c = c.replace('Te=Me.length>0,', 'Te=!!G,');
    console.log(path + ': Te condition updated');
    changed = true;
  } else {
    console.log(path + ': Te=Me.length>0 NOT FOUND');
  }

  // 2. Replace z handler
  if (c.includes(oldZ)) {
    c = c.replace(oldZ, newZ);
    console.log(path + ': z handler replaced');
    changed = true;
  } else {
    console.log(path + ': z handler NOT FOUND - trying partial match');
    // Try to find it
    const partial = 'z=()=>{if(!G||Me.length===0){X("Load API messages first.");return}';
    const idx = c.indexOf(partial);
    console.log('  partial at:', idx);
  }

  // 3. Rename label
  if (c.includes('children:"Rules Scan"')) {
    c = c.replace('children:"Rules Scan"', 'children:"AI Digest"');
    console.log(path + ': label renamed');
    changed = true;
  }

  // 4. Update tooltip
  if (c.includes('"Run local rules scan on selected account messages"')) {
    c = c.replace('"Run local rules scan on selected account messages"', '"Analyze your inbox with AI"');
    console.log(path + ': tooltip updated');
    changed = true;
  }

  if (changed) {
    fs.writeFileSync(path, c);
    console.log(path + ': SAVED');
  }
}
