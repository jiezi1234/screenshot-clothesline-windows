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
    // Observe the original component's hover boundary; resize only the native host.
    // The TSX itself owns all card visibility, animation, tilt, fetching and tooltips.
    const link = container.current!.querySelector("a")!;
    const hoverGroup = link.parentElement!.parentElement!;
    let timer: ReturnType<typeof setTimeout>;
    const send = (expanded: boolean) => window.chrome?.webview?.postMessage({ type: "github-size", expanded });
    const enter = () => { clearTimeout(timer); send(true); };
    const leave = () => { clearTimeout(timer); timer = setTimeout(() => send(false), 200); };
    hoverGroup.addEventListener("mouseenter", enter);
    hoverGroup.addEventListener("mouseleave", leave);
    window.cordnoteGithub = { sourceHash: __SOURCE_HASH__, component: "GreatUI/GithubCard.tsx" };
    window.chrome?.webview?.postMessage({ type: "github-ready", sourceHash: __SOURCE_HASH__ });
    return () => { clearTimeout(timer); hoverGroup.removeEventListener("mouseenter", enter); hoverGroup.removeEventListener("mouseleave", leave); };
  }, []);
  return <div id="github-host" ref={container}>
    <GithubCard username="jiezi1234" name="jiezi1234" avatarUrl={__AVATAR__}
      text={window.cordnoteLabel || "关注我 ·"} linkText="GitHub" year={new Date().getFullYear()}
      themeScheme="green" className="github-component" labelClassName="text-sm" linkClassName="text-sm" />
  </div>;
}
createRoot(document.getElementById("root")!).render(<App />);
