// Lightweight structured logger for the profile-client
// Usage:
//   import { createLogger } from "$lib/log";
//   const log = createLogger("category:sub");
//   log.info("loading", { id, input });
// Configuration (at runtime in browser via localStorage):
//   localStorage.setItem("pc.log.level", "debug"); // trace|debug|info|warn|error|silent
//   localStorage.setItem("pc.log.filter", "chain,verify"); // comma-separated substrings to include (optional)
// Defaults to INFO level and no filter (all categories enabled).

export type LogLevelName = "trace" | "debug" | "info" | "warn" | "error" | "silent";

const LEVEL_ORDER: Record<Exclude<LogLevelName, "silent">, number> = {
  trace: 10,
  debug: 20,
  info: 30,
  warn: 40,
  error: 50,
};

function nowTs(): string {
  try {
    return new Date().toISOString();
  } catch {
    return String(Date.now());
  }
}

function readLevel(): LogLevelName {
  if (typeof localStorage !== "undefined") {
    const raw = localStorage.getItem("pc.log.level")?.toLowerCase();
    if (raw === "trace" || raw === "debug" || raw === "info" || raw === "warn" || raw === "error" || raw === "silent") {
      return raw;
    }
  }
  return "info";
}

function readFilter(): string[] | null {
  if (typeof localStorage === "undefined") return null;
  const raw = localStorage.getItem("pc.log.filter");
  if (!raw) return null;
  return raw.split(",").map(s => s.trim()).filter(Boolean);
}

function levelEnabled(level: LogLevelName, min: LogLevelName): boolean {
  if (min === "silent") return false;
  if (level === "silent") return false;
  return LEVEL_ORDER[level as Exclude<LogLevelName, "silent">] >= LEVEL_ORDER[min as Exclude<LogLevelName, "silent">];
}

function passesFilter(category: string, filter: string[] | null): boolean {
  if (!filter || filter.length === 0) return true;
  const lc = category.toLowerCase();
  return filter.some(f => lc.includes(f.toLowerCase()));
}

export interface Logger {
  trace(msg: string, data?: any): void;
  debug(msg: string, data?: any): void;
  info(msg: string, data?: any): void;
  warn(msg: string, data?: any): void;
  error(msg: string, data?: any): void;
  // Helper to time an async operation
  time<T>(label: string, fn: () => Promise<T>, data?: any): Promise<T>;
}

export function createLogger(category: string): Logger {
  let counter = 0;
  function emit(level: Exclude<LogLevelName, "silent">, msg: string, data?: any): void {
    const min = readLevel();
    const filter = readFilter();
    if (!levelEnabled(level, min)) return;
    if (!passesFilter(category, filter)) return;

    const ts = nowTs();
    const base = `[ProfileClient] [${ts}] [${level.toUpperCase()}] [${category}]`;
    try {
      if (data !== undefined) {
        // Avoid throwing on circular structures
        // eslint-disable-next-line no-console
        (console as any)[level === "warn" ? "warn" : level === "error" ? "error" : "log"](base + " " + msg, data);
      } else {
        // eslint-disable-next-line no-console
        (console as any)[level === "warn" ? "warn" : level === "error" ? "error" : "log"](base + " " + msg);
      }
    } catch {
      // Swallow logging errors
    }
  }

  async function time<T>(label: string, fn: () => Promise<T>, data?: any): Promise<T> {
    const id = ++counter;
    const start = (typeof performance !== "undefined" && performance.now) ? performance.now() : Date.now();
    emit("debug", `${label} → start #${id}` , data);
    try {
      const result = await fn();
      const end = (typeof performance !== "undefined" && performance.now) ? performance.now() : Date.now();
      const ms = Math.round(end - start);
      emit("debug", `${label} ← ok #${id} (${ms} ms)`);
      return result;
    } catch (e: any) {
      const end = (typeof performance !== "undefined" && performance.now) ? performance.now() : Date.now();
      const ms = Math.round(end - start);
      emit("error", `${label} ← error #${id} (${ms} ms)`, { error: String(e?.message ?? e) });
      throw e;
    }
  }

  return {
    trace: (m, d) => emit("trace", m, d),
    debug: (m, d) => emit("debug", m, d),
    info:  (m, d) => emit("info",  m, d),
    warn:  (m, d) => emit("warn",  m, d),
    error: (m, d) => emit("error", m, d),
    time,
  };
}
