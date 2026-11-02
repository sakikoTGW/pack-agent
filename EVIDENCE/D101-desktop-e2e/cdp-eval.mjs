// Minimal CDP evaluate helper: node cdp-eval.mjs "<expression>"
const list = await fetch("http://127.0.0.1:9223/json/list").then((r) => r.json());
const page = list.find((t) => t.url.startsWith("dsh-app://app/")) ?? list[0];
if (!page) {
  console.error("no page target");
  process.exit(2);
}

const ws = new WebSocket(page.webSocketDebuggerUrl);
let nextId = 1;
const pending = new Map();

ws.addEventListener("message", (event) => {
  const msg = JSON.parse(event.data);
  if (msg.id && pending.has(msg.id)) {
    pending.get(msg.id)(msg);
    pending.delete(msg.id);
  }
});
await new Promise((resolve, reject) => {
  ws.addEventListener("open", resolve, { once: true });
  ws.addEventListener("error", reject, { once: true });
});

function send(method, params) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((resolve) => pending.set(id, resolve));
}

const expression = process.argv.slice(2).join(" ");
const reply = await send("Runtime.evaluate", {
  expression,
  returnByValue: true,
  awaitPromise: true,
});
const result = reply.result?.result;
console.log(JSON.stringify(result?.value !== undefined ? result.value : result));
ws.close();
