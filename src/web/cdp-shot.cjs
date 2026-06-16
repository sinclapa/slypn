const http = require('http');
const ws_mod = require('ws'); // check if ws is available

const PAGE_ID = '01FA50A7751B2CEA4E04424EB87AA7FD';
const WS_URL = `ws://localhost:9223/devtools/page/${PAGE_ID}`;

const ws = new ws_mod(WS_URL);
let id = 1;
const pending = new Map();

ws.on('message', (data) => {
  const msg = JSON.parse(data.toString());
  if (msg.id && pending.has(msg.id)) {
    pending.get(msg.id)(msg);
    pending.delete(msg.id);
  }
});

function send(method, params = {}) {
  return new Promise((resolve) => {
    const myId = id++;
    pending.set(myId, resolve);
    ws.send(JSON.stringify({ id: myId, method, params }));
  });
}

ws.on('open', async () => {
  await send('Page.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
  await new Promise(r => setTimeout(r, 2000));
  
  // Click Calendar button
  const click = await send('Runtime.evaluate', {
    expression: `(function(){
      const btn = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Calendar');
      if(btn){btn.click();return 'clicked';}return 'missing';
    })()`
  });
  process.stderr.write('click: ' + JSON.stringify(click.result) + '\n');
  await new Promise(r => setTimeout(r, 1000));
  
  const ss = await send('Page.captureScreenshot', { format: 'jpeg', quality: 70 });
  require('fs').writeFileSync('C:/tmp/cal-calendar.jpg', Buffer.from(ss.result.data, 'base64'));
  process.stderr.write('screenshot saved\n');
  ws.close();
});
