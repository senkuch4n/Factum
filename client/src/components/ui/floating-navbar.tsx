"use client";
import React, { useState, type ReactElement } from "react";
import {
  motion,
  AnimatePresence,
  useScroll,
  useMotionValueEvent,
} from "motion/react";
import { cn } from "@/lib/utils";

export type NavItem = {
  name: string;
  link?: string;
  icon?: ReactElement;
  onClick?: () => void;
};

export const FloatingNav = ({
  navItems,
  className,
  right,
  initialVisible = false,
}: {
  navItems: NavItem[];
  className?: string;
  /** Content that replaces the default "Login" CTA slot */
  right?: React.ReactNode;
  /** Show the navbar immediately at the top of the page */
  initialVisible?: boolean;
}) => {
  const { scrollYProgress } = useScroll();
  const [visible, setVisible] = useState(initialVisible);

  useMotionValueEvent(scrollYProgress, "change", (current) => {
    if (typeof current === "number") {
      const direction = current - scrollYProgress.getPrevious()!;

      if (scrollYProgress.get() < 0.05) {
        setVisible(initialVisible);
      } else {
        setVisible(direction < 0);
      }
    }
  });

  return (
    <AnimatePresence mode="wait">
      <motion.div
        initial={{ opacity: initialVisible ? 1 : 0, y: initialVisible ? 0 : -100 }}
        animate={{ y: visible ? 0 : -100, opacity: visible ? 1 : 0 }}
        transition={{ duration: 0.2 }}
        className={cn(
          "flex max-w-fit fixed top-10 inset-x-0 mx-auto z-[5000] items-center justify-center",
          className
        )}
      >
        <div className="flex items-center justify-center gap-2 rounded-full border border-white/10 bg-white/80 px-4 py-1.5 shadow-lg shadow-black/10 backdrop-blur-md dark:border-white/10 dark:bg-black/50">
          {/* Nav items */}
          <div className="flex items-center gap-1">
            {navItems.map((navItem, idx) => (
              <a
                key={`link-${idx}`}
                href={navItem.link ?? "#"}
                onClick={
                  navItem.onClick
                    ? (e) => { e.preventDefault(); navItem.onClick!(); }
                    : navItem.link ? undefined : (e) => e.preventDefault()
                }
                className="relative flex items-center gap-1.5 rounded-full px-4 py-2 text-sm font-medium text-neutral-600 transition-colors hover:bg-neutral-100 hover:text-neutral-900 dark:text-neutral-300 dark:hover:bg-white/10 dark:hover:text-white"
              >
                {navItem.icon && <span className="flex-shrink-0">{navItem.icon}</span>}
                <span className="hidden sm:block">{navItem.name}</span>
              </a>
            ))}
          </div>

          <div className="h-5 w-px bg-neutral-200 dark:bg-white/10" />

          {right !== undefined ? (
            right
          ) : (
            <button className="relative rounded-full bg-neutral-900 px-4 py-2 text-sm font-medium text-white transition-all hover:bg-neutral-800 dark:bg-white dark:text-black dark:hover:bg-neutral-100">
              Login
            </button>
          )}
        </div>
      </motion.div>
    </AnimatePresence>
  );
};
