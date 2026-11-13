import React, { useEffect, useRef } from "react";
import { createRoot } from "react-dom/client";
import GithubCard from "../vendor/GreatUI/GithubCard";

declare const __SOURCE_HASH__: string;
declare const __AVATAR__: string;
declare global { interface Window {
  chrome?: { webview?: { postMessage(message: unknown): void } };
  cordnoteGithub?: { sourceHash: string; component: string };
  cordnoteLabel?: string;
} }

function App() {
  const container = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const post = (message: unknown) => window.chrome?.webview?.postMessage(message);
    const link = container.current!.querySelector("a")!;
    const hoverGroup = link.parentElement!.parentElement!;
    let timer: ReturnType<typeof setTimeout>;
    const send = (expanded: boolean) => post({ type: "github-size", expanded });
    const enter = () => { clearTimeout(timer); send(true); };
    const leave = () => { clearTimeout(timer); timer = setTimeout(() => send(false), 200); };
    hoverGroup.addEventListener("mouseenter", enter);
    hoverGroup.addEventListener("mouseleave", leave);
    window.cordnoteGithub = { sourceHash: __SOURCE_HASH__, component: "GreatUI/GithubCard.tsx" };
    post({ type: "github-ready", sourceHash: __SOURCE_HASH__ });
    const openTimer = setTimeout(() => {
      hoverGroup.dispatchEvent(new MouseEvent("mouseover", { bubbles: true, relatedTarget: document.body }));
      hoverGroup.dispatchEvent(new MouseEvent("mouseenter", { bubbles: false, relatedTarget: document.body }));
    }, 50);
    const measureTimer = setTimeout(() => {
      const card = document.querySelector(".cordnote-card");
      if (card) {
        const rect = card.getBoundingClientRect();
        post({ type: "card-bounds", left: rect.left, top: rect.top, width: rect.width, height: rect.height });
        card.addEventListener("click", (event) => {
          event.preventDefault(); event.stopPropagation(); post({ type: "open-profile" });
        });
      }
      post({ type: "ready" });
    }, 600);
    return () => {
      clearTimeout(timer); clearTimeout(openTimer); clearTimeout(measureTimer);
      hoverGroup.removeEventListener("mouseenter", enter); hoverGroup.removeEventListener("mouseleave", leave);
    };
  }, []);
  return <div id="github-host" ref={container}>
    <GithubCard username="jiezi1234" name="jiezi1234" avatarUrl={__AVATAR__}
      text={window.cordnoteLabel || "Follow me on"} linkText="GitHub" year={new Date().getFullYear()}
      themeScheme="blue" className="github-component" labelClassName="cordnote-hide" linkClassName="cordnote-hide" popoverClassName="cordnote-card" defaultOpen maxDays={28} />
  </div>;
}
createRoot(document.getElementById("root")!).render(<App />);
