import { build } from "esbuild";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(fileURLToPath(import.meta.url));
process.chdir(root);
const source = await readFile("vendor/GreatUI/GithubCard.tsx");
const provenance = JSON.parse(await readFile("vendor/GreatUI/provenance.json", "utf8"));
const hash = createHash("sha256").update(source).digest("hex");
if (hash !== provenance.sha256) throw new Error("Vendored Great UI source changed; review and record upstream provenance before building.");
const avatar = "data:image/png;base64," + (await readFile("../Clothesline.App/Assets/github-jiezi1234.png")).toString("base64");
const donateQr = "data:image/jpeg;base64," + (await readFile("../Clothesline.App/Assets/donate-qr.jpg")).toString("base64");
await mkdir(".build", { recursive: true });
execFileSync(process.execPath, ["node_modules/@tailwindcss/cli/dist/index.mjs", "-i", "src/styles.css", "-o", ".build/style.css", "--minify"], { stdio: "inherit" });
const result = await build({ entryPoints: ["src/main.tsx"], bundle: true, minify: true, write: false, jsx: "automatic", target: "chrome120", legalComments: "inline",
  alias: { "@/lib/utils": path.join(root, "src/utils.ts"), "@/components/site/ThemeProvider": path.join(root, "src/ThemeProvider.ts") },
  define: { __SOURCE_HASH__: JSON.stringify(hash), __AVATAR__: JSON.stringify(avatar), __DONATE_QR__: JSON.stringify(donateQr), "process.env.NODE_ENV": '"production"' } });
const css = await readFile(".build/style.css", "utf8");
const html = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="great-ui-source-sha256" content="${hash}"><style>${css}</style></head><body><div id="root"></div><script>${result.outputFiles[0].text.replaceAll("</script", "<\\/script")}</script></body></html>`;
const output = "../Clothesline.App/Assets/GithubWeb";
await mkdir(output, { recursive: true });
await writeFile(path.join(output, "index.html"), html);
console.log(`Built original Great UI GithubCard.tsx (${hash}), ${Buffer.byteLength(html)} bytes`);

execFileSync(process.execPath, ["node_modules/@tailwindcss/cli/dist/index.mjs", "-i", "src/settings.css", "-o", ".build/settings.css", "--minify"], { stdio: "inherit" });
const settings = await build({
  entryPoints: ["src/settings.tsx"], bundle: true, minify: true, write: false, jsx: "automatic", target: "chrome120", legalComments: "inline",
  alias: { "@/registry/default/lib/utils": path.join(root, "src/utils.ts"), "@/registry/default/ui/spinner": path.join(root, "vendor/coss/spinner.tsx") },
  define: { "process.env.NODE_ENV": '"production"' },
});
const settingsCss = await readFile(".build/settings.css", "utf8");
const licenseFiles = ["vendor/coss/LICENSE", "vendor/ruixen/LICENSE.md", ...["@base-ui/react", "@base-ui/utils", "lucide-react", "class-variance-authority"].map(pkg => "node_modules/" + pkg + "/LICENSE")];
const settingsNotices = (await Promise.all(licenseFiles.map(file => readFile(file, "utf8")))).join("\n");
const settingsHtml = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><!-- ${settingsNotices.replaceAll("--", "—")} --><style>${settingsCss}</style></head><body><div id="root"></div><script>${settings.outputFiles[0].text.replaceAll("</script", "<\\/script")}</script></body></html>`;
await writeFile("../Clothesline.App/Assets/SettingsWeb.html", settingsHtml);
