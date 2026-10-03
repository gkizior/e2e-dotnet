// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

internal static class PageScript
{
    // Each element keeps its ref under a registry symbol for the life of the
    // document, so a ref the model saw stays valid in the next observation.
    // The host passes the next free number, so refs never repeat across
    // documents. The latest observation's elements are kept for Find.
    public const string Collect = """
        (seed) => {
          const refKey = Symbol.for("e2e.observation.ref");
          const elements = new Map();
          let next = seed;
          const stamp = (el) => {
            let id = el[refKey];
            if (id === undefined) {
              id = "e" + next;
              next++;
              el[refKey] = id;
            }
            elements.set(id, el);
            return id;
          };
          const skip = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEMPLATE"]);
          const leaves = new Set(["button", "link", "textbox", "checkbox", "radio", "combobox", "searchbox", "heading", "status", "image", "tab"]);
          const max = 500;
          let count = 0;
          const cut = (value, limit) => {
            const text = (value || "").replace(/\s+/g, " ").trim();
            return text.length > limit ? text.slice(0, limit) : text;
          };
          const hidden = (el) => {
            if (el.hasAttribute("hidden") || el.getAttribute("aria-hidden") === "true") return true;
            const style = getComputedStyle(el);
            return style.display === "none" || style.visibility === "hidden";
          };
          const roleOf = (el) => {
            const explicit = el.getAttribute("role");
            if (explicit) return explicit;
            const tag = el.tagName;
            if (tag === "BUTTON") return "button";
            if (tag === "A" && el.hasAttribute("href")) return "link";
            if (tag === "TEXTAREA") return "textbox";
            if (tag === "SELECT") return "combobox";
            if (tag === "IMG") return "image";
            if (tag === "NAV") return "navigation";
            if (tag === "LI") return "listitem";
            if (/^H[1-6]$/.test(tag)) return "heading";
            if (tag === "P") return "paragraph";
            if (tag === "INPUT") {
              const type = (el.getAttribute("type") || "text").toLowerCase();
              if (type === "hidden") return null;
              if (type === "checkbox") return "checkbox";
              if (type === "radio") return "radio";
              if (type === "button" || type === "submit" || type === "reset") return "button";
              if (type === "search") return "searchbox";
              return "textbox";
            }
            if (el.getAttribute("aria-live") || tag === "OUTPUT") return "status";
            return null;
          };
          const nameOf = (el, role) => {
            const aria = el.getAttribute("aria-label");
            if (aria) return cut(aria, 256);
            if (el.labels && el.labels.length) return cut(el.labels[0].innerText || "", 256);
            const labelledby = el.getAttribute("aria-labelledby");
            if (labelledby) {
              const text = labelledby.split(/\s+/).map((id) => document.getElementById(id)?.innerText || "").join(" ");
              if (text.trim()) return cut(text, 256);
            }
            if (role === "textbox" || role === "searchbox") return cut(el.getAttribute("placeholder") || "", 256);
            if (role) return cut(el.innerText || el.getAttribute("alt") || "", 256);
            return "";
          };
          const walk = (el, into) => {
            if (!el || skip.has(el.tagName) || count >= max) return;
            const role = roleOf(el);
            const testId = el.getAttribute("data-testid");
            if (role || testId) {
              count++;
              const type = (el.getAttribute("type") || "").toLowerCase();
              const secure = type === "password" || el.getAttribute("autocomplete") === "current-password";
              const node = {
                ref: stamp(el),
                role,
                name: nameOf(el, role),
                text: role && leaves.has(role) ? null : cut(el.innerText || "", 512),
                value: secure || !("value" in el) ? null : String(el.value ?? ""),
                testId,
                placeholder: el.getAttribute("placeholder"),
                inputPurpose: secure ? "password" : null,
                level: /^H[1-6]$/.test(el.tagName) ? Number(el.tagName.slice(1)) : null,
                disabled: !!el.disabled,
                checked: !!el.checked,
                hidden: hidden(el),
                secure,
                children: []
              };
              into.push(node);
              if (role && leaves.has(role)) return;
              for (const child of el.children) walk(child, node.children);
              return;
            }
            for (const child of el.children) walk(child, into);
          };
          const roots = [];
          if (document.body) walk(document.body, roots);
          window[Symbol.for("e2e.observation.elements")] = elements;
          return JSON.stringify({ next, roots });
        }
        """;

    public const string Find = """
        (id) => {
          const el = window[Symbol.for("e2e.observation.elements")]?.get(id);
          return el && el.isConnected ? el : null;
        }
        """;
}
