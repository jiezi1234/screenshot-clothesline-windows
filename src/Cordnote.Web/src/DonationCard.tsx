"use client";

import React, { useRef } from "react";
import { motion, useMotionValue, useSpring, useTransform } from "motion/react";
import { cn } from "@/lib/utils";

export interface DonationCardProps {
  image: string;
  open?: boolean;
  cardTiltMaxRotate?: number;
  className?: string;
  popoverClassName?: string;
}

export const DonationCard = ({
  image,
  open = false,
  cardTiltMaxRotate = 5,
  className,
  popoverClassName,
}: DonationCardProps) => {
  const cardRef = useRef<HTMLDivElement>(null);
  const x = useMotionValue(0);
  const y = useMotionValue(0);

  const mouseXSpring = useSpring(x, { stiffness: 300, damping: 20 });
  const mouseYSpring = useSpring(y, { stiffness: 300, damping: 20 });

  const rotateX = useTransform(mouseYSpring, (val) => {
    const pct = (val + 20) / 40;
    return cardTiltMaxRotate - pct * (2 * cardTiltMaxRotate);
  });
  const rotateY = useTransform(mouseXSpring, (val) => {
    const pct = (val + 20) / 40;
    return -cardTiltMaxRotate + pct * (2 * cardTiltMaxRotate);
  });

  const handleMouseMove = (e: React.MouseEvent) => {
    if (!cardRef.current) return;
    const rect = cardRef.current.getBoundingClientRect();
    x.set(((e.clientX - rect.left - rect.width / 2) / (rect.width / 2)) * 20);
    y.set(((e.clientY - rect.top - rect.height / 2) / (rect.height / 2)) * 20);
  };

  return (
    <div className={cn("flex items-center gap-1.5", className)}>
      <div className="relative flex w-max flex-col items-center [perspective:1000px]">
        <motion.div
          ref={cardRef}
          onMouseMove={handleMouseMove}
          initial="hidden"
          style={{
            x: mouseXSpring,
            rotateX,
            rotateY,
            transformStyle: "preserve-3d" as const,
          }}
          animate={open ? "visible" : "hidden"}
          variants={{
            hidden: {
              opacity: 0,
              y: 6,
              scale: 0.98,
              filter: "blur(2px)",
              pointerEvents: "none",
              transformOrigin: "bottom center",
              transition: {
                duration: 0.15,
                ease: "easeIn",
              },
            },
            visible: {
              opacity: 1,
              y: 0,
              scale: 1,
              filter: "blur(0px)",
              pointerEvents: "auto",
              transformOrigin: "bottom center",
              transition: {
                duration: 0.22,
                ease: [0.16, 1, 0.3, 1],
              },
            },
          }}
          className={cn(
            "absolute bottom-full z-50 mb-4 w-80 rounded-2xl border border-dashed border-neutral-300 bg-white/95 p-6 shadow-xl backdrop-blur-md transition-colors dark:border-neutral-800 dark:bg-neutral-950/80",
            popoverClassName,
          )}
        >
          <img
            src={image}
            alt="Donation QR code"
            draggable={false}
            className="donate-qr block w-full select-none rounded-md"
          />
          <span className="mt-2 block text-center font-mono text-[10px] text-neutral-500 dark:text-neutral-400">
            buy me a coffee~
          </span>
        </motion.div>
      </div>
    </div>
  );
};

export default DonationCard;
