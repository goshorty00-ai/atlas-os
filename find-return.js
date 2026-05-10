const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show what's BEFORE and AFTER the return statement
const retPos = 21084; // 0-indexed position of 'r' in 'return'
console.log('Cols 21050-21130:');
console.log(JSON.stringify(line235.substring(21050, 21130)));

// Also look at the IMMEDIATE context of the outer flex-col div closing
// To understand the closing sequence
// The outer div should close somewhere, followed by IIFE's ;, then })(
// Let me find the sequence "]);})():"
const iifEnd = line235.indexOf('});})():');
console.log('\nIIFE end sequence "});})():" at col:', iifEnd+1, '(0-indexed:', iifEnd, ')');
console.log('Context:', JSON.stringify(line235.substring(iifEnd-20, iifEnd+50)));

// Also find "]})})]})]});" or similar
const closeSeq = line235.substring(26620, 26685);
console.log('\nClosing sequence cols 26620-26685:');
console.log(JSON.stringify(closeSeq));
