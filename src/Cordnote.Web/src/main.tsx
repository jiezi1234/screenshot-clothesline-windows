import React, { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import GithubCard from "../vendor/GreatUI/GithubCard";

declare const __AVATAR__: string;
declare global { interface Window {
  chrome?: { webview?: {
    postMessage(message: unknown): void;
    addEventListener(type: string, handler: (event: MessageEvent) => void): void;
    removeEventListener(type: string, handler: (event: MessageEvent) => void): void;
  } };
  cordnoteLabel?: string;
} }

function App() {
  const container = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const post = (message: unknown) => window.chrome?.webview?.postMessage(message);
  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      const message = event.data as { type?: string; open?: boolean; x?: number; y?: number } | undefined;
      if (message?.type === "card-open") {
        setOpen(message.open === true);
        return;
      }
      if (message?.type !== "card-pointer") return;
      const card = document.querySelector<HTMLElement>(".cordnote-card");
      if (!card) return;
      const rect = card.getBoundingClientRect();
      const x = Math.max(-1, Math.min(1, message.x ?? 0));
      const y = Math.max(-1, Math.min(1, message.y ?? 0));
      card.dispatchEvent(new MouseEvent("mousemove", {
        bubbles: true,
        clientX: rect.left + ((x + 1) * rect.width) / 2,
        clientY: rect.top + ((y + 1) * rect.height) / 2,
      }));
    };
    window.chrome?.webview?.addEventListener("message", onMessage);
    const measureTimer = setTimeout(() => {
      const card = document.querySelector<HTMLElement>(".cordnote-card");
      if (card) {
        const parent = card.offsetParent as HTMLElement | null;
        post({
          type: "card-bounds",
          left: (parent?.offsetLeft ?? 0) + card.offsetLeft,
          top: (parent?.offsetTop ?? 0) + card.offsetTop,
          width: card.offsetWidth,
          height: card.offsetHeight,
        });
        card.addEventListener("click", (event) => {
          event.preventDefault(); event.stopPropagation(); post({ type: "open-profile" });
        });
      }
      post({ type: "ready" });
    }, 600);
    return () => {
      clearTimeout(measureTimer);
      window.chrome?.webview?.removeEventListener("message", onMessage);
    };
  }, []);
  return <div id="github-host" ref={container}>
    <GithubCard username="jiezi1234" name="jiezi1234" avatarUrl={__AVATAR__}
      text={window.cordnoteLabel || "Follow me on"} linkText="GitHub" year={new Date().getFullYear()}
      themeScheme="blue" className="github-component" labelClassName="cordnote-hide" linkClassName="cordnote-hide" popoverClassName="cordnote-card" maxDays={56} open={open} />
  </div>;
}
createRoot(document.getElementById("root")!).render(<App />);
