import React, { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { Check, ChevronDown, FolderOpen, RefreshCw } from "lucide-react";
import { Card } from "../vendor/coss/card";
import { Button } from "../vendor/coss/button";
import { Input } from "../vendor/coss/input";
import { Switch } from "../vendor/coss/switch";
import { selectTriggerVariants } from "../vendor/coss/select";
import { MediaLibraries, type MediaSnapshot } from "./media";

type Value = string | boolean | number | null;
type Values = Record<string, Value>;
type Field = { id: string; label: string; help: string; kind: string; choices?: string[] };
type Group = { id: string; title: string; fields: Field[] };
type Configuration = { type: "init"; lang: string; browse: string; groups: Group[] };
type Message = Configuration | { type: "focus"; id: string } | { type: "draft"; version: number; values: Values }
  | { type: "info"; values: Values } | ({ type: "library" } & MediaSnapshot);
const host = window.chrome?.webview;
const labels: Record<string, [string, string]> = {
  sources: ["来源目录", "Folders"], collection: ["收集", "Collection"], shortcuts: ["快捷键与启动", "Shortcuts & startup"], appearance: ["外观", "Appearance"],
  screenshot: ["截图", "Screenshots"], video: ["录像", "Recordings"], extra0: ["额外目录 1", "Folder 1"], extra1: ["额外目录 2", "Folder 2"], extra2: ["额外目录 3", "Folder 3"],
  clipboard: ["收集剪贴板", "Collect clipboard"], popup: ["新项目时展开", "Open on capture"], full: ["全屏时收起", "Hide in full screen"],
  retention: ["保留历史", "Keep history"], storage: ["存储上限", "Storage limit"], capacity: ["晾衣绳容量", "Shelf capacity"],
  shortcutEnabled: ["全局快捷键", "Global shortcuts"], showShortcut: ["展开", "Show"], hideShortcut: ["收起", "Hide"],
  shortcutStatus: ["注册状态", "Registration"], login: ["登录时启动", "Start at sign-in"],
  cardSize: ["卡片大小", "Card size"], protection: ["展开保护", "Stay open"], motion: ["减少动画", "Reduce motion"],
  sound: ["提示音", "Capture sound"], language: ["语言", "Language"],
};

function Settings() {
  const [config, setConfig] = useState<Configuration>();
  const [values, setValues] = useState<Values>({});
  const [media, setMedia] = useState<MediaSnapshot>({ shelf: [], history: [] });
  const version = useRef(0);
  const panel = useRef<HTMLElement>(null);
  useEffect(() => {
    function receive(event: MessageEvent<Message>) {
      const message = event.data;
      switch (message.type) {
        case "init": document.documentElement.lang = message.lang; setConfig(message); break;
        case "focus": {
          const field = document.getElementById(message.id);
          const control = field?.closest(".field")?.querySelector<HTMLElement>('[role="switch"]') ?? field;
          control?.focus({ preventScroll: true });
          break;
        }
        case "library": setMedia({ shelf: message.shelf, history: message.history }); break;
        case "draft": version.current = message.version; setValues(current => ({ ...current, ...message.values })); break;
        case "info": setValues(current => ({ ...current, ...message.values })); break;
      }
    }
    host?.addEventListener("message", receive);
    host?.postMessage({ type: "ready" });
    return () => host?.removeEventListener("message", receive);
  }, []);

  useLayoutEffect(() => {
    const root = panel.current;
    if (!root || !config) return;
    function fit() {
      const scale = Math.min(1, window.innerWidth / 790, window.innerHeight / 440);
      if (scale <= 0) return;
      root!.style.zoom = String(scale);
      root!.style.width = Math.floor(window.innerWidth / scale) + "px";
      root!.style.height = Math.floor(window.innerHeight / scale) + "px";
    }
    fit(); window.addEventListener("resize", fit);
    return () => window.removeEventListener("resize", fit);
  }, [config]);

  useEffect(() => {
    if (!config) return;
    host?.postMessage({ type: "mounted" });
    const keydown = (event: KeyboardEvent) => {
      if (!event.defaultPrevented && !event.isComposing && event.key === "Escape" && !document.querySelector('[role="menu"]'))
        host?.postMessage({ type: "hide" });
    };
    window.addEventListener("keydown", keydown);
    return () => window.removeEventListener("keydown", keydown);
  }, [config]);

  if (!config) return null;
  const en = config.lang === "en";
  const t = (zh: string, english: string) => en ? english : zh;
  const label = (f: Field | Group) => labels[f.id]?.[en ? 1 : 0] ?? ("label" in f ? f.label : f.title);
  const activate = (id: string) => host?.postMessage({ type: "activate", version: version.current, id });
  function edit(id: string, value: Value) {
    setValues(current => ({ ...current, [id]: value }));
    host?.postMessage({ type: "edit", version: version.current, id, value });
  }
  function choiceText(id: string, original: string, index: number) {
    if (id === "retention") return [t("永久", "Forever"), t("7 天", "7 days"), t("30 天", "30 days")][index] ?? original;
    if (id === "cardSize") return ["80%", "100%", "120%"][index] ?? original;
    return id === "protection" ? original.split(/[（(]/)[0].trim() : original;
  }
  function field(f: Field) {
    if (f.id === "retry") return null;
    const value = values[f.id];
    const help = f.help || f.label;
    const common = { id: f.id, title: help, "aria-description": help };
    let control: React.ReactNode;
    if (f.kind === "switch") control = <Switch {...common} checked={value === true} onCheckedChange={v => edit(f.id, v)} />;
    else if (f.kind === "choice") control = <div className="choice">
      <select {...common} className={selectTriggerVariants()} value={String(value ?? -1)} onChange={event => edit(f.id, Number(event.target.value))}>
        {(value == null || value === -1) && <option value="-1" />}
        {f.choices?.map((name, index) => <option value={index} key={index}>{choiceText(f.id, name, index)}</option>)}
      </select><ChevronDown aria-hidden="true" /></div>;
    else if (f.id === "capacity") control = <output id={f.id} title={[value, help].join("\n")} aria-label={String(value ?? "")}>
      {typeof values.capacityCount === "number" ? t(values.capacityCount + " 项", values.capacityCount + " items") : "—"}</output>;
    else if (f.id === "shortcutStatus") {
      const state = values.shortcutState;
      const summary = state === "off" ? t("已关闭", "Off") : state === "failed" ? t("注册失败", "Conflict") : "—";
      const retry = t("重新注册快捷键", "Retry shortcut registration");
      control = <div className="registration"><output id={f.id} title={String(value ?? "")} aria-label={String(value ?? "")}>{state === "registered" ? <Check size={15} aria-hidden="true" /> : summary}</output>
        <Button size="icon-xs" variant="ghost" id="retry" title={retry} aria-label={retry} onClick={() => activate("retry")}><RefreshCw /></Button></div>;
    } else {
      const folder = f.kind === "folder";
      control = <div className={folder ? "input-wrap folder-input" : "input-wrap"}>
        <Input {...common} nativeInput autoComplete="off" spellCheck={false} value={String(value ?? "")}
          title={folder && value ? String(value) + "\n" + help : help}
          disabled={(f.id === "showShortcut" || f.id === "hideShortcut") && !values.shortcutEnabled}
          onChange={event => edit(f.id, event.target.value)} />
        {folder && <Button variant="ghost" size="icon-xs" title={f.label + " · " + config!.browse} aria-label={f.label + " · " + config!.browse} onClick={() => activate(f.id)}><FolderOpen /></Button>}
      </div>;
    }
    return <div key={f.id} className={"field field-" + f.kind} data-field={f.id}>
      <label htmlFor={f.id} title={help}>{label(f)}</label>{control}
    </div>;
  }
  return <main ref={panel}>
    <MediaLibraries media={media} lang={config.lang} reduceMotion={values.motion === true}
      send={(scope, id, action) => host?.postMessage({ type: "media", scope, id, action })} />
    <div className="settings-sheet">{config.groups.map(group => {
      return <Card key={group.id} className="settings-group" render={<section id={group.id} aria-labelledby={group.id + "-title"} />}>
        <h2 id={group.id + "-title"}>{label(group)}</h2>
        <div className="fields">{group.fields.map(field)}</div>
      </Card>;
    })}</div>
  </main>;
}
createRoot(document.getElementById("root")!).render(<Settings />);
