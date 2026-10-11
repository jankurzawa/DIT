const { AdobeBridge } = require("./dist/bridge.js");
const ppro = require("premierepro");
const { entrypoints, storage } = require("uxp");
let bridge;
let restoring=false;
const status = message => { document.getElementById("status").textContent = message; };
const failure = (stage, error) => {
  const message = (stage + ": " + (error.message || String(error))).replace(/[A-F0-9]{64}/gi, "[klucz]");
  console.error("DIT 0.1.21 | " + message);
  status(message);
};
async function connect(key) {
  status("Sprawdzanie połączenia z DIT…");
  const candidate = new AdobeBridge(ppro, key, status);
  await candidate.checkConnection();
  if (bridge) bridge.stop();
  bridge = candidate;
  void bridge.start();
}
async function restore() {
  if (bridge || restoring) return;
  restoring=true;
  try {
    let bytes;
    try {bytes = await storage.secureStorage.getItem("dit-connection");}
    catch(error){failure("Nie udało się odczytać zapisanego klucza Adobe. Wybierz plik połączenia",error);return;}
    if (bytes && bytes.length) await connect(Array.from(bytes, byte => String.fromCharCode(byte)).join(""));
    else status("Pierwsze użycie: wybierz plik DIT.ditconnection przygotowany w aplikacji DIT.");
  } catch (error) { failure("Nie udało się połączyć z DIT. Uruchom DIT i kliknij Wznów kolejkę",error); }
  finally { restoring=false; }
}
// Register actions before host lifecycle setup so a host initialization error cannot leave dead buttons.
document.getElementById("connect").addEventListener("click", async () => {
  try {
    const file = await storage.localFileSystem.getFileForOpening({ types: ["ditconnection"] });
    if (!file) return;
    const connection = JSON.parse(await file.read());
    if (typeof connection.key !== "string" || !/^[A-F0-9]{64}$/.test(connection.key)) throw Error("Niepoprawny plik połączenia DIT.");
    await connect(connection.key);
    await storage.secureStorage.setItem("dit-connection", connection.key);
    try { await file.delete(); } catch { /* Read-only transport file. */ }
  } catch (error) { failure("Nie udało się sparować panelu",error); }
});
document.getElementById("resume").addEventListener("click", () => { if (bridge) void bridge.start(); else void restore(); });
document.getElementById("stop").addEventListener("click", () => { if (bridge) bridge.stop(); });
try {
  entrypoints.setup({
    panels: { ditPanel: { create() {}, show() { void restore(); } } },
    plugin: { create() {}, destroy() { if (bridge) bridge.stop(); } }
  });
  void restore();
} catch (error) { status("Nie udało się uruchomić panelu DIT: " + error.message); }
