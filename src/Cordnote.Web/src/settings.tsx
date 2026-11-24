import React, { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { FolderOpen, Keyboard, SlidersHorizontal, PanelsTopLeft, ChevronDown, FolderSearch } from "lucide-react";
import { Card } from "../vendor/coss/card";
import { Button } from "../vendor/coss/button";
import { Input } from "../vendor/coss/input";
import { Switch } from "../vendor/coss/switch";
import { selectTriggerVariants } from "../vendor/coss/select";

type Value = string | boolean | number | null;
type Values = Record<string, Value>;
type Field = { id: string; label: string; help: string; kind: string; choices?: string[] };
type Group = { id: string; title: string; fields: Field[] };
type Configuration = { type: "init"; lang: string; browse: string; groups: Group[] };
type Message = Configuration | { type: "focus"; id: string } | { type: "draft"; version: number; values: Values } | { type: "info"; values: Values };
const host = window.chrome?.webview;
const icons = { sources: FolderOpen, collection: SlidersHorizontal, shortcuts: Keyboard, appearance: PanelsTopLeft };

function Settings() {
  const [config, setConfig] = useState<Configuration>();
  const [values, setValues] = useState<Values>({});
  const version = useRef(0);
  const panel = useRef<HTMLElement>(null);
  useEffect(() => {
    function receive(event: MessageEvent<Message>) {
      const message = event.data;
      if (message.type === "init") {
        document.documentElement.lang = message.lang;
        setConfig(message);
      } else if (message.type === "focus") {
        document.getElementById(message.id)?.focus({ preventScroll: true });
      } else if (message.type === "draft") {
        version.current = message.version;
        setValues(message.values);
      } else if (message.type === "info") {
        setValues(current => ({ ...current, ...message.values }));
      }
    }
    host?.addEventListener("message", receive);
    host?.postMessage({ type: "ready" });
    return () => host?.removeEventListener("message", receive);
  }, []);

  useLayoutEffect(() => {
    if (!config || !panel.current) return;
    const root = panel.current;
    function fit() {
      // Measure at a legible four-column width, then fit the available client area at high DPI or small window sizes.
      const width = Math.max(window.innerWidth, 900);
      root.style.zoom = "1";
      root.style.width = width + "px";
      root.style.minHeight = "0";
      let scale = Math.min(1, window.innerWidth / width, window.innerHeight / Math.max(1, root.offsetHeight));
      if (scale <= 0) return;
      // Zoom can reflow translated labels at fractional pixel boundaries.
      for (let pass = 0; pass < 4; pass++) {
        root.style.zoom = String(scale);
        root.style.width = Math.floor(window.innerWidth / scale) + "px";
        const height = root.getBoundingClientRect().height;
        if (height <= window.innerHeight) break;
        scale *= (window.innerHeight - 2) / height;
      }
      root.style.minHeight = Math.floor(window.innerHeight / scale) + "px";
    }
    fit();
    window.addEventListener("resize", fit);
    return () => window.removeEventListener("resize", fit);
  }, [config, values.capacity, values.shortcutStatus]);

  useEffect(() => {
    if (!config) return;
    host?.postMessage({ type: "mounted" });
    const keydown = (e: KeyboardEvent) => {
      if (!e.defaultPrevented && !e.isComposing && e.key === "Escape") host?.postMessage({ type: "hide" });
    };
    window.addEventListener("keydown", keydown);
    return () => window.removeEventListener("keydown", keydown);
  }, [config]);

  function edit(id: string, value: Value) {
    setValues(current => ({ ...current, [id]: value }));
    host?.postMessage({ type: "edit", version: version.current, id, value });
  }
  const activate = (id: string) => host?.postMessage({ type: "activate", version: version.current, id });

  function field(f: Field) {
    const value = values[f.id];
    const description = f.help ? f.id + "-help" : undefined;
    const common = { id: f.id, title: f.help || undefined, "aria-describedby": description };
    const help = f.help && <p id={description} className="sr-only">{f.help}</p>;
    if (f.kind === "switch") return <div className="field" title={f.help || undefined} key={f.id}>
      <div className="switch-row"><label htmlFor={f.id}>{f.label}</label>
        <Switch {...common} checked={value === true} onCheckedChange={v => edit(f.id, v)} /></div>{help}</div>;
    if (f.kind === "info") return <div className="field" title={f.help || undefined} key={f.id}><p className="field-label">{f.label}</p><p className="info-value">{value}</p>{help}</div>;
    if (f.kind === "button") return <div key={f.id}><Button variant="outline" id={f.id} onClick={() => activate(f.id)}>{f.label}</Button></div>;
    return <div className="field" title={f.help || undefined} key={f.id}>
      <label htmlFor={f.id}>{f.label}</label>
      {f.kind === "choice" ? <div className="choice">
        <select {...common} className={selectTriggerVariants()} value={String(value ?? -1)} onChange={e => edit(f.id, Number(e.target.value))}>
          {(value == null || value === -1) && <option value="-1" />}
          {f.choices?.map((label, index) => <option key={index} value={index}>{label}</option>)}
        </select><ChevronDown size={14} aria-hidden="true" /></div> :
        <div className="input-row"><Input {...common} autoComplete="off" spellCheck={false} value={String(value ?? "")}
          disabled={(f.id === "showShortcut" || f.id === "hideShortcut") && !values.shortcutEnabled}
          onChange={e => edit(f.id, e.target.value)} />
          {f.kind === "folder" && <Button variant="outline" title={f.label + " · " + config?.browse} aria-label={f.label + " · " + config?.browse} onClick={() => activate(f.id)}><FolderSearch size={15} aria-hidden="true" /></Button>}</div>}
      {help}
    </div>;
  }
  return <main ref={panel}>{config?.groups.map(group => {
    const Icon = icons[group.id as keyof typeof icons];
    return <Card key={group.id} render={<section id={group.id} aria-labelledby={group.id + "-title"} />}>
      <header><span className="icon"><Icon size={18} strokeWidth={1.7} /></span><h2 id={group.id + "-title"}>{group.title}</h2></header>
      <div className="fields">{group.fields.map(field)}</div>
    </Card>;
  })}</main>;
}
createRoot(document.getElementById("root")!).render(<Settings />);
