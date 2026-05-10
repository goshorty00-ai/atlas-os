// Testing the template literal structure
const vt = `Draft reply
Hi,

Thanks for your message about "${}". I reviewed the details and will follow up shortly with the next steps.

Best regards,`;

if (typeof vt === 'string') {
  console.log('Template literal is valid! Length:', vt.length);
  console.log('First 50 chars:', vt.substring(0, 50));
  console.log('Last 30 chars:', vt.substring(vt.length - 30));
} else {
  console.log('Error: vt is not a string');
}
