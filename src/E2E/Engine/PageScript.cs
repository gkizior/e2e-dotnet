// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

internal static class PageScript
{
    // Each element keeps its ref under a registry symbol for the life of the
    // document, so a ref the model saw stays valid in the next observation.
    // The host passes the next free number, so refs never repeat across
    // documents. The latest observation's elements are kept for Find. The
    // walk reads one document: it goes through open shadow roots and closed
    // ones the init script recorded, and lists an iframe as a boundary node
    // the host fills with that frame's own observation. When the document has
    // more nodes than the budget, every node that intersects the viewport is
    // kept first and the rest of the budget goes to the others in document
    // order, so scrolling brings the cut ones in. It says when it cut nodes.
    public const string Collect = """
        ({ seed, max, testIdAttribute }) => {
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
          const leaves = new Set(["button", "link", "textbox", "checkbox", "radio", "searchbox", "heading", "status", "image", "tab",
            "option", "menuitem", "menuitemcheckbox", "menuitemradio", "switch", "slider", "spinbutton", "progressbar", "meter", "separator", "iframe"]);
          const maxSelectOptions = 60;
          let count = 0;
          let full = false;
          let offBudget = max;
          let offCount = 0;
          let scroll = Math.round(scrollX) + "," + Math.round(scrollY);
          const closedRoots = globalThis[Symbol.for("e2e.closedShadowRoots")];
          const shadowOf = (el) => el.shadowRoot ?? closedRoots?.get(el) ?? null;
          let focused = document.activeElement;
          while (focused) {
            const inner = shadowOf(focused)?.activeElement ?? null;
            if (!inner) break;
            focused = inner;
          }
          const hasOwnName = (el) => ["aria-label", "aria-labelledby", "title"].some((attribute) => (el.getAttribute(attribute) || "").trim() !== "");
          const pageLevel = (el) => el.closest("article, aside, main, nav, section, [role~=article], [role~=complementary], [role~=main], [role~=navigation], [role~=region]") === null;
          // An option paints inside its select, so it is on screen when the select is.
          const inView = (el) => {
            const box = el.tagName === "OPTION" ? (el.closest("select") ?? el) : el;
            const r = box.getBoundingClientRect();
            return r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth;
          };
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
            const explicit = (el.getAttribute("role") || "").trim();
            if (explicit) {
              const first = explicit.split(/\s+/)[0];
              return first === "img" ? "image" : first;
            }
            const tag = el.tagName;
            switch (tag) {
              case "A": return el.hasAttribute("href") ? "link" : null;
              case "BUTTON": return "button";
              case "SELECT": return el.multiple || el.size > 1 ? "listbox" : "combobox";
              case "TEXTAREA": return "textbox";
              case "IMG": return el.getAttribute("alt") === "" ? "presentation" : "image";
              case "NAV": return "navigation";
              case "MAIN": return "main";
              case "ASIDE": return "complementary";
              case "HEADER": return pageLevel(el) ? "banner" : null;
              case "FOOTER": return pageLevel(el) ? "contentinfo" : null;
              case "SECTION": return hasOwnName(el) ? "region" : null;
              case "FORM": return hasOwnName(el) ? "form" : null;
              case "ARTICLE": return "article";
              case "FIGURE": return "figure";
              case "HR": return "separator";
              case "PROGRESS": return "progressbar";
              case "METER": return "meter";
              case "MENU": case "UL": case "OL": return "list";
              case "FIELDSET": case "DETAILS": case "OPTGROUP": case "ADDRESS": return "group";
              case "OPTION": return "option";
              case "LI": return "listitem";
              case "TABLE": return "table";
              case "THEAD": case "TBODY": case "TFOOT": return "rowgroup";
              case "TR": return "row";
              case "TD": {
                const tableRole = (el.closest("table")?.getAttribute("role") || "").trim().split(/\s+/)[0];
                return tableRole === "grid" || tableRole === "treegrid" ? "gridcell" : "cell";
              }
              case "TH": {
                const scope = (el.getAttribute("scope") || "").toLowerCase();
                return scope === "row" || scope === "rowgroup" ? "rowheader" : "columnheader";
              }
              case "DIALOG": return "dialog";
              case "OUTPUT": return "status";
              case "P": return "paragraph";
              case "IFRAME": return "iframe";
              case "INPUT": {
                const type = (el.getAttribute("type") || "text").toLowerCase();
                if (type === "hidden") return null;
                if (el.list && ["text", "search", "tel", "url", "email"].includes(el.type)) return "combobox";
                if (type === "checkbox") return "checkbox";
                if (type === "radio") return "radio";
                if (["button", "submit", "reset", "image", "file"].includes(type)) return "button";
                if (type === "range") return "slider";
                if (type === "number") return "spinbutton";
                if (type === "search") return "searchbox";
                return "textbox";
              }
            }
            if (/^H[1-6]$/.test(tag)) return "heading";
            if (el.getAttribute("aria-live")) return "status";
            return null;
          };
          // accname reads an aria-labelledby reference (2B) before the
          // element's own aria-label (2C), then its associated labels.
          const nameOf = (el, role) => {
            const labelledby = (el.getAttribute("aria-labelledby") || "").trim();
            if (labelledby) {
              const text = labelledby.split(/\s+/).map((id) => document.getElementById(id)?.innerText || "").join(" ");
              if (text.trim()) return cut(text, 256);
            }
            const aria = el.getAttribute("aria-label");
            if (aria && aria.trim()) return cut(aria, 256);
            if (el.labels && el.labels.length) return cut(el.labels[0].innerText || "", 256);
            if (role === "textbox" || role === "searchbox") return cut(el.getAttribute("placeholder") || "", 256);
            if (role) return cut(el.innerText || el.getAttribute("alt") || "", 256);
            return "";
          };
          // The roles aria-disabled applies to (WAI-ARIA 1.2), as Playwright's
          // toBeDisabled reads them.
          const ariaDisabledRoles = new Set([
            "application", "button", "composite", "gridcell", "group", "input", "link", "menuitem", "scrollbar",
            "separator", "tab", "checkbox", "columnheader", "combobox", "grid", "listbox", "menu", "menubar",
            "menuitemcheckbox", "menuitemradio", "option", "radio", "radiogroup", "row", "rowheader", "searchbox",
            "select", "slider", "spinbutton", "switch", "tablist", "textbox", "toolbar", "tree", "treegrid", "treeitem"
          ]);
          const parentOrHostOf = (el) => {
            if (el.parentElement) return el.parentElement;
            const root = el.getRootNode();
            return root instanceof ShadowRoot ? root.host : null;
          };
          // The nearest aria-disabled on the element or above it; "false" cuts the chain.
          const ariaDisabledInChain = (el) => {
            for (let current = el; current; current = parentOrHostOf(current)) {
              const value = (current.getAttribute("aria-disabled") || "").toLowerCase();
              if (value === "true") return true;
              if (value === "false") return false;
            }
            return false;
          };
          // :disabled covers the control's own attribute, a disabled fieldset
          // (outside its first legend), and a disabled optgroup. aria-disabled
          // on the element counts for any role; inherited, it reaches only the
          // roles the state applies to.
          const disabledOf = (el, role) => {
            if (el.matches(":disabled")) return true;
            if (el.getAttribute("aria-disabled") === "true") return true;
            return !!role && ariaDisabledRoles.has(role) && ariaDisabledInChain(el);
          };
          // A native checkbox or radio reports its own state; aria-checked
          // fills in only where the element has no native state.
          const checkedOf = (el) => {
            if (el instanceof HTMLInputElement && (el.type === "checkbox" || el.type === "radio")) return el.checked;
            return el.getAttribute("aria-checked") === "true";
          };
          const attributesOf = (el, secure) => {
            const out = {};
            for (const attr of el.attributes) {
              if (secure && attr.name === "value") continue;
              out[attr.name] = attr.value;
            }
            return out;
          };
          const rectOf = (el) => {
            const box = el.getBoundingClientRect();
            return { x: box.x, y: box.y, width: box.width, height: box.height };
          };
          const listed = (el, role) => (role && role !== "presentation" && role !== "none") || !!el.getAttribute(testIdAttribute);
          // How many nodes the walk would list, or only those on screen.
          const tally = (el, visible) => {
            if (!el || skip.has(el.tagName)) return 0;
            if (el.tagName === "IFRAME" && hidden(el)) return 0;
            let n = 0;
            const role = roleOf(el);
            if (listed(el, role)) {
              const counted = !visible || inView(el);
              if (counted) n++;
              if (el.tagName === "SELECT") return counted ? n + Math.min(el.options.length, maxSelectOptions) : n;
              if (role && leaves.has(role)) return n;
            }
            for (const child of el.children) n += tally(child, visible);
            const shadow = shadowOf(el);
            if (shadow) for (const child of shadow.children) n += tally(child, visible);
            return n;
          };
          // A node under a hidden ancestor is hidden too.
          const walk = (el, into, parentHidden) => {
            if (!el || skip.has(el.tagName) || full) return;
            const isHidden = !!parentHidden || hidden(el);
            const role = roleOf(el);
            const testId = el.getAttribute(testIdAttribute);
            const isFrame = el.tagName === "IFRAME";
            if (isFrame && isHidden) return;
            if (listed(el, role)) {
              if (count >= max) {
                full = true;
                return;
              }
              if (offBudget < max && !inView(el)) {
                if (offCount >= offBudget) {
                  if ((role && leaves.has(role)) || el.tagName === "SELECT") return;
                  walkChildren(el, into, isHidden);
                  return;
                }
                offCount++;
              }
              count++;
              const type = (el.getAttribute("type") || "").toLowerCase();
              const secure = type === "password" || el.getAttribute("autocomplete") === "current-password";
              const node = {
                ref: stamp(el),
                role,
                name: isFrame ? cut(el.getAttribute("title") || "", 256) : nameOf(el, role),
                text: role && leaves.has(role) ? null : cut(el.innerText || "", 512),
                value: secure || !("value" in el) || el.tagName === "OPTION" ? null : String(el.value ?? ""),
                testId,
                placeholder: el.getAttribute("placeholder"),
                inputPurpose: secure ? "password" : null,
                level: /^H[1-6]$/.test(el.tagName) ? Number(el.tagName.slice(1)) : null,
                disabled: disabledOf(el, role),
                checked: checkedOf(el),
                expanded: el.getAttribute("aria-expanded") === "true" || (el.tagName === "DETAILS" && el.open),
                selected: el.tagName === "OPTION" ? !!el.selected : el.getAttribute("aria-selected") === "true",
                pressed: el.getAttribute("aria-pressed") === "true",
                focused: el === focused,
                hidden: isHidden,
                secure,
                frame: isFrame,
                attributes: attributesOf(el, secure),
                rect: rectOf(el),
                children: []
              };
              into.push(node);
              if (el.scrollTop || el.scrollLeft) scroll += ";" + node.ref + ":" + Math.round(el.scrollLeft) + "," + Math.round(el.scrollTop);
              if (el.tagName === "SELECT") {
                // A closed select paints none of its options; they are what it offers.
                for (const option of Array.from(el.options).slice(0, maxSelectOptions)) {
                  const before = node.children.length;
                  walk(option, node.children, isHidden);
                  if (node.children.length > before) node.children[before].hidden = isHidden;
                }
                return;
              }
              if (role && leaves.has(role)) return;
              walkChildren(el, node.children, isHidden);
              return;
            }
            walkChildren(el, into, isHidden);
          };
          // Light children first, then the shadow tree. Slotted elements are
          // light children and the shadow tree holds only their slots, so
          // nothing is listed twice.
          const walkChildren = (el, into, parentHidden) => {
            for (const child of el.children) walk(child, into, parentHidden);
            const shadow = shadowOf(el);
            if (shadow) for (const child of shadow.children) walk(child, into, parentHidden);
          };
          const roots = [];
          const truncated = !!document.body && tally(document.body, false) > max;
          if (truncated) offBudget = Math.max(0, max - tally(document.body, true));
          if (document.body) walk(document.body, roots, false);
          window[Symbol.for("e2e.observation.elements")] = elements;
          return JSON.stringify({ next, count, truncated: truncated || full, scroll, roots });
        }
        """;

    // Moves about three quarters of a screen, so every row shows at least once.
    public const string ScrollViewport = """
        (direction) => {
          const x = Math.round(innerWidth * 0.75);
          const y = Math.round(innerHeight * 0.75);
          const by = { up: [0, -y], down: [0, y], left: [-x, 0], right: [x, 0] }[direction];
          window.scrollBy({ left: by[0], top: by[1], behavior: "instant" });
        }
        """;

    public const string ScrollElement = """
        (el, direction) => {
          const x = Math.round(el.clientWidth * 0.75);
          const y = Math.round(el.clientHeight * 0.75);
          const by = { up: [0, -y], down: [0, y], left: [-x, 0], right: [x, 0] }[direction];
          el.scrollBy({ left: by[0], top: by[1], behavior: "instant" });
        }
        """;

    // The ref an iframe element carries, so the host can hang that frame's
    // observation under the iframe node.
    public const string RefOf = """
        (el) => el[Symbol.for("e2e.observation.ref")] ?? null
        """;

    // Context init script that keeps closed shadow roots reachable for
    // Collect. attachShadow is the one way a script makes such a root;
    // declarative closed roots are parsed, not attached, and stay out of reach.
    public const string RecordClosedShadowRoots = """
        (() => {
          const key = Symbol.for("e2e.closedShadowRoots");
          if (Object.prototype.hasOwnProperty.call(globalThis, key)) return;
          const roots = new WeakMap();
          Object.defineProperty(globalThis, key, { value: roots, enumerable: false, configurable: false, writable: false });
          const attachShadow = Element.prototype.attachShadow;
          Element.prototype.attachShadow = function (init) {
            const root = attachShadow.call(this, init);
            if (root.mode === "closed") roots.set(this, root);
            return root;
          };
        })();
        """;

    public const string Find = """
        (id) => {
          const el = window[Symbol.for("e2e.observation.elements")]?.get(id);
          return el && el.isConnected ? el : null;
        }
        """;
}
