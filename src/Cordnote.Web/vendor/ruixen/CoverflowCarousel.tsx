// Adapted from Ruixen UI's Coverflow Carousel (MIT), https://ruixen.com/docs/components/coverflow-carousel.
// Keep the fractional position, perspective projection and release-to-snap interaction.
// Cordnote supplies the covers, stable library IDs, caption and file commands.
import React, { useLayoutEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "../coss/button";

type Props<T extends { id: string }> = {
  slides: T[];
  id: string;
  label: string;
  previous: string;
  next: string;
  reduceMotion: boolean;
  empty: React.ReactNode;
  renderCover: (slide: T) => React.ReactNode;
  renderCaption: (slide: T | undefined, index: number) => React.ReactNode;
  onOpen: (slide: T) => void;
  onShortcut: (event: React.KeyboardEvent, slide: T) => void;
};

const wrap = (value: number, count: number) => ((value % count) + count) % count;
type Drag = { pointer: number; x: number; start: number; pitch: number; last: number; time: number; velocity: number; offset: number; hitId?: string; moved: boolean };

export function CoverflowCarousel<T extends { id: string }>({
  slides, id, label, previous, next, reduceMotion, empty, renderCover, renderCaption, onOpen, onShortcut,
}: Props<T>) {
  const count = slides.length;
  const loop = count > 2;
  const [position, setPosition] = useState(0);
  const current = useRef(0);
  const target = useRef(0);
  const selected = useRef<string | undefined>(undefined);
  const frame = useRef(0);
  const drag = useRef<Drag | null>(null);
  const stage = useRef<HTMLDivElement>(null);
  const lastTap = useRef<string | undefined>(undefined);

  function draw(value: number) {
    current.current = value;
    setPosition(value);
  }
  function bound(value: number) {
    return loop ? value : Math.max(0, Math.min(count - 1, value));
  }
  function settle(value: number) {
    if (!count) return;
    cancelAnimationFrame(frame.current);
    const to = bound(Math.round(value));
    target.current = to;
    const normalized = wrap(to, count);
    selected.current = slides[normalized].id;
    if (reduceMotion || window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      target.current = normalized;
      draw(normalized);
      return;
    }
    const from = current.current;
    const start = performance.now();
    function tick(now: number) {
      const t = Math.min(1, (now - start) / 270);
      if (t === 1) { target.current = normalized; draw(normalized); return; }
      draw(to + (from - to) * Math.pow(1 - t, 3));
      frame.current = requestAnimationFrame(tick);
    }
    frame.current = requestAnimationFrame(tick);
  }

  useLayoutEffect(() => {
    const found = slides.findIndex(slide => slide.id === selected.current);
    const index = found >= 0 ? found : Math.max(0, Math.min(Math.round(current.current), count - 1));
    selected.current = slides[index]?.id;
    target.current = index;
    drag.current = null;
    draw(index);
    return () => cancelAnimationFrame(frame.current);
  }, [slides]);

  const index = count ? wrap(Math.round(position), count) : 0;
  const active = slides[index];
  function begin(event: React.PointerEvent<HTMLDivElement>) {
    if (!count || event.button !== 0 || drag.current) return;
    const surface = event.currentTarget;
    const cover = surface.querySelector<HTMLElement>("[data-cover]");
    if (!cover) return;
    cancelAnimationFrame(frame.current);
    const hit = (event.target as Element).closest<HTMLElement>("[data-cover]");
    const scale = surface.getBoundingClientRect().width / surface.offsetWidth;
    drag.current = { pointer: event.pointerId, x: event.clientX, start: current.current, pitch: cover.offsetWidth * scale * .84,
      last: event.clientX, time: event.timeStamp, velocity: 0, offset: Number(hit?.dataset.offset ?? 0), hitId: hit?.dataset.cover, moved: false };
    surface.focus({ preventScroll: true });
    surface.setPointerCapture(event.pointerId);
  }
  function move(event: React.PointerEvent) {
    const d = drag.current;
    if (!d || d.pointer !== event.pointerId) return;
    const distance = event.clientX - d.x;
    if (!d.moved && Math.abs(distance) < 4) return;
    d.moved = true;
    const elapsed = Math.max(1, event.timeStamp - d.time);
    d.velocity = (event.clientX - d.last) / elapsed / d.pitch;
    d.time = event.timeStamp;
    d.last = event.clientX;
    draw(bound(d.start - distance / d.pitch));
  }
  function release(event: React.PointerEvent, cancelled = false) {
    const d = drag.current;
    if (!d || d.pointer !== event.pointerId) return;
    drag.current = null;
    lastTap.current = d.moved || cancelled ? undefined : d.hitId;
    const fling = !cancelled && event.timeStamp - d.time < 90 ? Math.max(-2, Math.min(2, d.velocity * 125)) : 0;
    settle(d.moved || cancelled ? current.current - fling : d.start + d.offset);
  }

  // Mount each nearby entry once. A long history costs the same per frame as a short shelf.
  const covers = [];
  const center = Math.round(position);
  for (let slot = center - 4; slot <= center + 4 && count > 0; slot++) {
    const offset = slot - position;
    if (loop ? offset <= -count / 2 || offset > count / 2 : slot < 0 || slot >= count) continue;
    const slide = slides[wrap(slot, count)];
    const distance = Math.abs(offset);
    const ramp = Math.pow(distance, .56);
    const tilt = -Math.sign(offset) * Math.min(44 * ramp, 82);
    const seam = loop ? Math.min(1, (count / 2 - distance) * 3) : 1;
    covers.push(<div className="cover" data-cover={slide.id} data-offset={offset} data-active={slide.id === active?.id}
      key={slide.id} aria-hidden={slide.id !== active?.id} style={{
        transform: `translate3d(calc(-50% + var(--cover-width) * ${offset * .84}), -50%, calc(var(--cover-width) * ${-.6 * ramp})) rotateY(${tilt}deg)`,
        opacity: Math.max(0, (1 - .10 * distance) * seam), zIndex: 10 - Math.round(distance),
      }}>{renderCover(slide)}</div>);
  }

  return <>
    <div className="rack">
      <div id={id} ref={stage} className="stage" tabIndex={0} role="group" aria-label={label}
        onPointerDown={begin} onPointerMove={move} onPointerUp={event => release(event)}
        onPointerCancel={event => release(event, true)} onLostPointerCapture={event => release(event, true)}
        onDoubleClick={() => {
          const tapped = slides.find(slide => slide.id === lastTap.current);
          if (tapped) onOpen(tapped);
        }}
        onKeyDown={event => {
          if (count && (event.key === "ArrowLeft" || event.key === "ArrowRight")) {
            event.preventDefault(); settle(target.current + (event.key === "ArrowLeft" ? -1 : 1));
          } else if (active && event.key === "Enter") { event.preventDefault(); onOpen(active); }
          else if (active) onShortcut(event, active);
        }}>
        {count ? covers : empty}
      </div>
      {count > 1 && <div className="rack-navigation">
        <Button variant="outline" size="icon-sm" title={previous} aria-label={previous} disabled={!loop && index === 0} onClick={() => settle(target.current - 1)}><ChevronLeft /></Button>
        <Button variant="outline" size="icon-sm" title={next} aria-label={next} disabled={!loop && index === count - 1} onClick={() => settle(target.current + 1)}><ChevronRight /></Button>
      </div>}
    </div>
    {renderCaption(active, index)}
  </>;
}
