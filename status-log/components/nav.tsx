"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

const links = [
  { href: "/", label: "Today" },
  { href: "/settings", label: "Settings" },
  { href: "/usage", label: "Usage" },
];

export function Nav() {
  const pathname = usePathname();
  return (
    <header className="border-b">
      <div className="mx-auto flex max-w-4xl items-center gap-1 px-4 py-3">
        <Link href="/" className="mr-4 font-semibold tracking-tight">
          📋 Status Log
        </Link>
        <nav className="flex items-center gap-1">
          {links.map((l) => {
            const active =
              l.href === "/" ? pathname === "/" : pathname.startsWith(l.href);
            return (
              <Link
                key={l.href}
                href={l.href}
                className={cn(
                  "rounded-md px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
                  active && "bg-muted font-medium text-foreground",
                )}
              >
                {l.label}
              </Link>
            );
          })}
        </nav>
      </div>
    </header>
  );
}
