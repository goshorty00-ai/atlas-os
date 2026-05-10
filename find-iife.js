const fs=require('fs');
const code=fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js','utf8');
const lines=code.split('\n');
const line235=lines[234];

// Show code around where the children: prop is to understand the full structure
console.log('Cols 18650-18780:');
console.log(line235.substring(18650, 18780));

// Also look at cols 26640-26680 to see closing structure
console.log('\nCols 26640-26680:');
console.log(JSON.stringify(line235.substring(26640, 26680)));

// And find what the IIFE returns exactly
// The IIFE should start with (function(){
// Let's find it
const iife = '?(function(){';
let iifPos = line235.indexOf(iife, 18700);
console.log('\nIIFE starts at col:', iifPos+1, '(0-indexed:', iifPos, ')');
console.log('Context at IIFE start:', JSON.stringify(line235.substring(iifPos-10, iifPos+50)));

// Search for the return statement inside IIFE
// The IIFE should have "return o.jsxs("div",{className:"flex flex-col h-full"
const returnMatch = 'return o.jsxs("div",{className:"flex flex-col h-full';
let retPos = line235.indexOf(returnMatch, iifPos);
console.log('\nReturn statement at col:', retPos+1);
console.log('Context:', JSON.stringify(line235.substring(retPos-5, retPos+60)));
