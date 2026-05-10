const fs = require('fs');

const code = fs.readFileSync('Figma/Email/dist/assets/index-C4iwRHDc.js', 'utf8');

// Try parsing with Function constructor to get actual error
try {
  eval(code);
  console.log('File parses successfully!');
} catch (e) {
  console.log('Error:', e.message);
  console.log('Stack:', e.stack);
  
  // Try to extract line number from error
  const match = e.stack.match(/<anonymous>:(\d+):(\d+)/);
  if (match) {
    const errLine = parseInt(match[1]) - 1;
    const errCol = parseInt(match[2]);
    console.log(`\nError at line ${errLine + 1}, column ${errCol}`);
    
    const lines = code.split('\n');
    if (lines[errLine]) {
      const line = lines[errLine];
      const start = Math.max(0, errCol - 100);
      const end = Math.min(line.length, errCol + 100);
      
      console.log(`\nLine ${errLine + 1} (length: ${line.length}):`);
      console.log('...' + line.substring(start, end) + '...');
      console.log(' '.repeat(errCol - start + 3) + '^ ERROR');
    }
  }
}
