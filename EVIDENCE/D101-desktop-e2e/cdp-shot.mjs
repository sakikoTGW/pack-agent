// Capture a PNG from the official desktop renderer over CDP.
// usage: node cdp-shot.mjs <out.png>
import { writeFileSync } from "node:fs";

const out = process.argv[2] ?? "shot.png";
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
const send = (method, params) => {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((resolve) => pending.set(id, resolve));
};

await send("Page.enable", {});
const shot = await send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false });
const data = shot.result?.data;
if (!data) {
  console.error(JSON.stringify(shot));
  process.exit(3);
}
writeFileSync(out, Buffer.from(data, "base64"));
console.log(out);
ws.close();
