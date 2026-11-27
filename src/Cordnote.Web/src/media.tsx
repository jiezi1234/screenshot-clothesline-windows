import React, { useEffect, useMemo, useState } from "react";
import { Archive, ArchiveRestore, Copy, Ellipsis, Eye, FileQuestionMark, FileText, Film, FolderOpen,
  History, Image as ImageIcon, Images, Pencil, Search, Trash2, Unlink, Upload } from "lucide-react";
import { Menu } from "@base-ui/react/menu";
import { Button } from "../vendor/coss/button";
import { Card } from "../vendor/coss/card";
import { Input } from "../vendor/coss/input";
import { CoverflowCarousel } from "../vendor/ruixen/CoverflowCarousel";

type Slide = { id: string; title: string; path: string; kind: "image" | "video" | "text"; missing: boolean; detail: string; src: string | null; textVersion?: string };
export type MediaSnapshot = { shelf: Slide[]; history: Slide[] };
type Scope = keyof MediaSnapshot;
type Command = "preview" | "copy" | "restore" | "edit" | "export" | "reveal" | "archive" | "archiveAll" | "forget" | "recycle";
type Translate = (zh: string, en: string) => string;

function TextCover({ slide, t }: { slide: Slide; t: Translate }) {
  const [text, setText] = useState<string | null>();
  useEffect(() => {
    const host = window.chrome?.webview;
    const receive = (event: MessageEvent) => {
      const message = event.data;
      if (message.type === "text-preview" && message.id === slide.id && message.version === slide.textVersion)
        setText(message.text);
    };
    host?.addEventListener("message", receive);
    host?.postMessage({ type: "text-preview", id: slide.id, version: slide.textVersion });
    return () => host?.removeEventListener("message", receive);
  }, [slide.id, slide.textVersion]);
  return <div className="file-cover note-cover"><p>{text ?? (text === null ? t("无法读取文字", "Text unavailable") : slide.title)}</p></div>;
}

function Cover({ slide, t }: { slide: Slide; t: Translate }) {
  const [failed, fail] = useState<string | null>(null);
  if (slide.kind === "text" && !slide.missing) return <TextCover key={slide.textVersion} slide={slide} t={t} />;
  if (slide.src && slide.src !== failed) return <img src={slide.src} alt={slide.title} draggable={false} onError={() => fail(slide.src)} />;
  const Icon = slide.missing ? FileQuestionMark : slide.kind === "text" ? FileText : slide.kind === "video" ? Film : ImageIcon;
  const label = slide.missing ? t("文件失效", "File missing") : slide.kind === "text" ? t("文字", "Text") : t("无预览", "No preview");
  return <div className="file-cover"><Icon strokeWidth={1.25} /><span>{label}</span></div>;
}

function Actions({ scope, slide, t, run }: { scope: Scope; slide: Slide; t: Translate; run: (command: Command) => void }) {
  const shelf = scope === "shelf";
  const primary = shelf ? t("复制", "Copy") : t("重新挂上", "Restore");
  const PrimaryIcon = shelf ? Copy : ArchiveRestore;
  const entries = [
    { action: "export", title: t("导出文件", "Export file"), Icon: Upload, show: !slide.missing },
    { action: "reveal", title: t("打开所在文件夹", "Show in folder"), Icon: FolderOpen, show: !slide.missing },
    { action: "archive", title: t("取下到历史", "Move to history"), Icon: Archive, show: shelf },
    { action: "archiveAll", title: t("全部取下", "Move all to history"), Icon: Archive, show: shelf },
    { action: "forget", title: t("移除记录", "Remove record"), Icon: Unlink, show: !shelf },
    { action: "recycle", title: t("移到回收站", "Move to Recycle Bin"), Icon: Trash2, show: !shelf && !slide.missing },
  ] as const;
  return <div className="media-actions">
    <Button size="sm" variant="outline" data-action={shelf ? "copy" : "restore"} disabled={shelf && slide.missing} onClick={() => run(shelf ? "copy" : "restore")}><PrimaryIcon />{primary}</Button>
    <Button size="icon-sm" variant="ghost" data-action="preview" title={t("预览", "Preview")} aria-label={t("预览", "Preview")} disabled={slide.missing} onClick={() => run("preview")}><Eye /></Button>
    {slide.kind === "image" && <Button size="icon-sm" variant="ghost" data-action="edit" title={t("编辑图片", "Edit image")} aria-label={t("编辑图片", "Edit image")} disabled={slide.missing} onClick={() => run("edit")}><Pencil /></Button>}
    <Menu.Root modal={false}>
      <Menu.Trigger render={<Button size="icon-sm" variant="ghost" title={t("更多操作", "More actions")} aria-label={t("更多操作", "More actions")} />}><Ellipsis /></Menu.Trigger>
      <Menu.Portal><Menu.Positioner side="bottom" align="end" sideOffset={4} className="menu-position">
        <Menu.Popup className="action-menu">
          {entries.filter(entry => entry.show).map(({ action, title, Icon }) =>
            <Menu.Item key={action} data-action={action} onClick={() => run(action)}><Icon size={15} />{title}</Menu.Item>)}
        </Menu.Popup>
      </Menu.Positioner></Menu.Portal>
    </Menu.Root>
  </div>;
}

export function MediaLibraries({ media, lang, reduceMotion, send }: {
  media: MediaSnapshot; lang: string; reduceMotion: boolean; send: (scope: Scope, id: string, action: Command) => void;
}) {
  const [query, setQuery] = useState("");
  const history = useMemo(() => {
    const text = query.trim().toLocaleLowerCase(lang);
    return text ? media.history.filter(item => item.title.toLocaleLowerCase(lang).includes(text) || item.path.toLocaleLowerCase(lang).includes(text)) : media.history;
  }, [query, media.history, lang]);
  const t: Translate = (zh, en) => lang === "en" || lang.startsWith("en-") ? en : zh;
  return <div className="libraries">
    {(["shelf", "history"] as const).map(scope => {
      const shelf = scope === "shelf";
      const slides = shelf ? media.shelf : history;
      const title = shelf ? t("晾衣绳", "Clothesline") : t("本地历史", "History");
      const Icon = shelf ? Images : History;
      const filtered = !shelf && !!query.trim();
      const emptyTitle = filtered ? t("无匹配记录", "No matches") : shelf ? t("暂无项目", "No items") : t("暂无记录", "No history");
      const EmptyIcon = filtered ? Search : shelf ? ImageIcon : Archive;
      return <Card className="library" key={scope} render={<section aria-labelledby={scope + "-title"} data-library={scope} />}>
        <header className="library-header"><h2 id={scope + "-title"}><Icon />{title}</h2>
          {!shelf && <div className="search-box"><Search size={14} aria-hidden="true" />
            <Input nativeInput type="search" id="history-search" aria-label={t("搜索历史", "Search history")} placeholder={t("搜索", "Search")} value={query} onChange={event => setQuery(event.target.value)} /></div>}
        </header>
        <CoverflowCarousel slides={slides} id={scope + "-stage"} label={title} previous={t("上一项", "Previous item")} next={t("下一项", "Next item")}
          reduceMotion={reduceMotion} renderCover={slide => <Cover slide={slide} t={t} />}
          empty={<div className="gallery-empty">
            <EmptyIcon size={15} aria-hidden="true" />
            <p>{emptyTitle}</p>
          </div>}
          onOpen={slide => { if (!slide.missing) send(scope, slide.id, "preview"); }}
          onShortcut={(event, slide) => {
            if (!shelf) return;
            if (event.key === "Delete" && !event.ctrlKey && !event.altKey && !event.shiftKey) { event.preventDefault(); send(scope, slide.id, "archive"); }
            if (event.ctrlKey && event.key.toLowerCase() === "c" && !slide.missing) { event.preventDefault(); send(scope, slide.id, "copy"); }
          }}
          renderCaption={(slide, index) => slide && <div className="caption" data-selected={slide.id}>
            <div className="caption-text" aria-live="polite" aria-atomic="true" title={[slide.path, slide.detail].filter(Boolean).join("\n")}>
              <span className="file-title">{slide.title}</span><span className="counter">{index + 1} / {slides.length}</span>
            </div>
            <Actions key={slide.id} scope={scope} slide={slide} t={t} run={action => send(scope, slide.id, action)} />
          </div>} />
      </Card>;
    })}
  </div>;
}
