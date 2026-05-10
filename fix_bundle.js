const fs = require('fs');
const path = require('path');

const bundlePath = path.join(__dirname, 'Figma', 'Email', 'dist', 'assets', 'index-C4iwRHDc.js');

try {
  console.log('Reading bundle file...');
  let content = fs.readFileSync(bundlePath, 'utf-8');
  const originalLength = content.length;
  console.log(`Original length: ${originalLength}`);

  // Strategy: Remove the _html ternary conditional entirely, keeping only the plaintext rendering
  // Pattern: _html?...[:...]  
  // We need to find this and replace with just the second part (plaintext)
  
  // Find and remove the _html variable definition first
  const htmlVarPattern = /,_html=\(typeof _d\.htmlBody==="string"\?_d\.htmlBody:""\)/g;
  content = content.replace(htmlVarPattern, '');
  console.log('Removed _html variable definition');

  // Now find the conditional rendering and replace with plaintext only
  // The pattern is complex because of nested ternaries, so let's use a simple approach:
  // Find "_html?" and replace the ternary with just the second branch
  
  const ternaryStart = content.indexOf('_html?');
  if (ternaryStart !== -1) {
    console.log(`Found _html? at position ${ternaryStart}`);
    
    // Extract context around it to understand the structure
    const contextStart = Math.max(0, ternaryStart - 100);
    const contextEnd = Math.min(content.length, ternaryStart + 500);
    const context = content.substring(contextStart, contextEnd);
    console.log('Context around error:\n' + context.substring(0, 300));
    
    // Simple replacement: _html?A:B becomes just B (the plaintext rendering)
    // Find matching brackets for this ternary
    let pos = ternaryStart + 6; // skip "_html?"
    let braceCount = 0;
    let parenCount = 0;
    let inTrueBranch = true;
    let trueStart = pos;
    let falseStart = -1;
    let falseEnd = -1;
    
    while (pos < content.length && falseEnd === -1) {
      const char = content[pos];
      
      if (char === '{') braceCount++;
      else if (char === '}') braceCount--;
      else if (char === '(') parenCount++;
      else if (char === ')') parenCount--;
      else if (char === ':' && braceCount === 0 && parenCount === 0) {
        // Found the colon separating ternary branches
        inTrueBranch = false;
        falseStart = pos + 1;
        console.log(`Found ternary colon at ${pos}`);
      }
      else if (char === ',' && braceCount === 0 && parenCount === 0 && falseStart !== -1) {
        // Found the end of the false branch
        falseEnd = pos;
        console.log(`Found ternary end at ${falseEnd}`);
        break;
      }
      
      pos++;
    }
    
    if (falseStart !== -1 && falseEnd !== -1) {
      const falseBranch = content.substring(falseStart, falseEnd);
      const newContent = content.substring(0, ternaryStart) + falseBranch + content.substring(falseEnd);
      content = newContent;
      console.log(`Replaced ternary with false branch`);
    }
  }

  console.log(`Final length: ${content.length}`);
  console.log(`Removed ${originalLength - content.length} bytes`);
  
  fs.writeFileSync(bundlePath, content, 'utf-8');
  console.log('File updated successfully');
  
  // Also update the bin/x64 copy
  const binBundlePath = path.join(__dirname, 'bin', 'x64', 'Figma', 'Email', 'dist', 'assets', 'index-C4iwRHDc.js');
  fs.copyFileSync(bundlePath, binBundlePath);
  console.log('Bin bundle updated');
  
} catch (error) {
  console.error('Error:', error.message);
  process.exit(1);
}
