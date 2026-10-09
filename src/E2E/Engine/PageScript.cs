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
          let textCount = 0;
          let textCut = false;
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
          // A shadow tree sits inside its host's sections, so the scope goes up through the hosts.
          const pageLevel = (el) => {
            for (let current = el; current; ) {
              if (current.closest("article, aside, main, nav, section, [role~=article], [role~=complementary], [role~=main], [role~=navigation], [role~=region]") !== null) return false;
              const root = current.getRootNode();
              current = root instanceof ShadowRoot ? root.host : null;
            }
            return true;
          };
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
          // Content a closed details folds away: anything under it outside its
          // summary. Every closed ancestor is asked, nested details included.
          const inClosedDetails = (el) => {
            for (let details = el.parentElement?.closest("details"); details; details = details.parentElement?.closest("details")) {
              if (!details.open) {
                const summary = details.querySelector(":scope > summary");
                if (!summary || !summary.contains(el)) return true;
              }
            }
            return false;
          };
          // A text node that lays out to a box a person can see.
          const visibleText = (node) => {
            const range = document.createRange();
            range.selectNode(node);
            const box = range.getBoundingClientRect();
            return box.width > 0 && box.height > 0;
          };
          // Render visibility, as Playwright reads it: display: none, a
          // visibility other than visible, and content a closed details folds
          // away. A display: contents element has no box; it is shown when a
          // child element or its own text is. Two parts are not in it, since
          // role queries and the agent read the same hidden state as the
          // accessibility tree: a box with no size, which an empty landmark
          // has, and aria-hidden, which the walk adds though the node paints.
          const hidden = (el, style = getComputedStyle(el)) => {
            if (style.display === "contents") {
              // A host paints its shadow tree, so that counts as its children too.
              const children = [...el.childNodes, ...(shadowOf(el)?.childNodes ?? [])];
              for (const child of children) {
                if (child.nodeType === 1 && !skip.has(child.tagName) && !hidden(child)) return false;
                if (child.nodeType === 3 && style.visibility === "visible" && visibleText(child)) return false;
              }
              return true;
            }
            return style.display === "none" || style.visibility !== "visible" || inClosedDetails(el);
          };
          const ariaHidden = (el) => el.getAttribute("aria-hidden") === "true";
          // What hides the element and everything under it: an aria-hidden
          // subtree, which the accessibility tree drops; display: none, which
          // no descendant can undo; and content-visibility: hidden, which keeps
          // the element's own box and renders nothing under it. visibility is
          // not in it, since a child may set visibility: visible and paint again.
          const hidesSubtree = (el, style) => ariaHidden(el) || style.display === "none" || style.contentVisibility === "hidden";
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
              // An inline icon is a picture whether or not anything names it, as
              // Playwright reads it; Chrome ignores an unnamed one.
              case "svg": return "image";
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
                if (scope === "row" || scope === "rowgroup") return "rowheader";
                if (scope === "col" || scope === "colgroup") return "columnheader";
                const previous = el.previousElementSibling;
                const next = el.nextElementSibling;
                if (previous === null && next === null) {
                  const row = el.parentElement;
                  const table = row?.tagName === "TR" ? row.closest("table") : null;
                  return table !== null && table.rows.length <= 1 ? null : "columnheader";
                }
                if (previous?.tagName === "TH" && next?.tagName === "TH") return "columnheader";
                const hasDataNeighbor = [previous, next].some((cell) =>
                  cell?.tagName === "TD" && ((cell.textContent || "").trim() !== "" || cell.children.length > 0));
                return hasDataNeighbor ? "rowheader" : "columnheader";
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
          // The accessible name, by upstream's accname rules. A walk's
          // inReference is set inside an aria-labelledby traversal, where a
          // nested reference is not followed (2B); hiddenAllowed when that
          // traversal began at a hidden target, whose whole subtree counts (2A);
          // visited holds every element already read, so each contributes once.
          // accessible is set for an accessible name, which reads CSS generated
          // content and an embedded control's value, and unset for label text.
          const nameWalk = (visited, accessible) => ({ inReference: false, hiddenAllowed: false, visited: new Set(visited), accessible });
          // Icon-font glyphs (private-use code points) read as nothing a person could type; each becomes a space.
          const iconGlyphs = /\p{Co}/gu;
          const withoutGlyphs = (name) => {
            const spaced = name.replace(iconGlyphs, " ");
            return spaced === name ? name : spaced.replace(/\s+/g, " ").trim();
          };
          // A name of icon glyphs alone falls back to the element's title, else to no name.
          const reportedName = (el, name) => {
            if (name === null) return null;
            const shown = withoutGlyphs(name);
            if (shown !== "" || shown === name) return shown;
            const title = (el.getAttribute("title") || "").trim();
            return title === "" ? null : title;
          };
          // The text a content value contributes: its strings and attr() values,
          // or only the alternative text after a "/". Anything else contributes nothing.
          const contentTextOf = (el, value) => {
            const tokens = [];
            const token = /\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|[-\w]+\((?:[^()"']|"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')*\)|\/|[-\w]+)/gy;
            let match;
            let end = 0;
            while ((match = token.exec(value)) !== null) {
              tokens.push(match[1]);
              end = token.lastIndex;
            }
            if (value.slice(end).trim() !== "") return null;
            let text = "";
            for (const part of tokens.slice(tokens.lastIndexOf("/") + 1)) {
              const attribute = /^attr\(\s*([-\w]+)\s*\)$/.exec(part);
              if (attribute !== null) text += el.getAttribute(attribute[1]) ?? "";
              else if (part.startsWith('"') || part.startsWith("'")) {
                text += part.slice(1, -1).replace(/\\([0-9a-fA-F]{1,6})\s?|\\(.)/g, (_all, hex, char) =>
                  hex === undefined ? char : String.fromCodePoint(Number.parseInt(hex, 16)));
              } else return null;
            }
            return text;
          };
          // What an element's ::before or ::after adds to a name (accname 2F.ii), spaced like a block when it is not inline.
          const generatedContentOf = (el, pseudo) => {
            const style = getComputedStyle(el, pseudo);
            if (style.display === "none" || style.visibility === "hidden") return "";
            const value = style.content;
            if (value === "" || value === "none" || value === "normal") return "";
            const text = contentTextOf(el, value);
            if (text === null) return "";
            return style.display === "inline" ? text : " " + text + " ";
          };
          // Nested named-from-content roles walk the same descendants again, so
          // each element's style, and each pseudo element, is read once.
          const styles = new Map();
          const styleOf = (el) => {
            let style = styles.get(el);
            if (!style) styles.set(el, style = getComputedStyle(el));
            return style;
          };
          const generated = new Map();
          const generatedOf = (el, pseudo) => {
            let entry = generated.get(el);
            if (!entry) generated.set(el, entry = {});
            if (!(pseudo in entry)) entry[pseudo] = generatedContentOf(el, pseudo);
            return entry[pseudo];
          };
          // A subtree the name computation drops: aria-hidden, or hidden by style as innerText leaves it out.
          const isNameHidden = (el, style) =>
            el.getAttribute("aria-hidden") === "true" || style.display === "none" || style.visibility === "hidden";
          // alt of an element HTML-AAM names by it: an img or an input type="image".
          const altOf = (el) => {
            const named = el instanceof HTMLImageElement || (el instanceof HTMLInputElement && el.type === "image");
            const alt = named ? el.getAttribute("alt") : null;
            return alt !== null && alt.trim() !== "" ? alt.trim() : null;
          };
          // The <title> child an svg is named by (SVG-AAM), as Playwright reads
          // it. An svg marked presentation or none takes no name of its own from
          // it, though the title still names a link or button around it.
          const svgTitleOf = (el) => {
            if (!(el instanceof SVGElement)) return null;
            const title = Array.from(el.children).find((child) => child instanceof SVGTitleElement);
            const text = (title?.textContent ?? "").replace(/\s+/g, " ").trim();
            return text === "" ? null : text;
          };
          const isPresentational = (el) => {
            const role = roleOf(el);
            return role === "presentation" || role === "none";
          };
          // A referenced target accname 2A reads whole: hidden itself, or under an aria-hidden ancestor.
          // Upstream's hidden includes a box with no size, which a target under display: none has.
          const isReferenceHidden = (el) => {
            if (hidden(el) || el.closest("[aria-hidden=\"true\"]") !== null) return true;
            const box = el.getBoundingClientRect();
            return !(box.width > 0 && box.height > 0);
          };
          // An IDREF resolves in the tree scope it is written in.
          const referencedElementOf = (el, id) => {
            const root = el.getRootNode();
            return root instanceof Document || root instanceof DocumentFragment ? root.getElementById(id) : null;
          };
          // Each aria-labelledby target's contribution (accname 2B), in attribute
          // order, unnamed targets dropped; null when their joined text is empty.
          const referencedNamesOf = (el, walk) => {
            if (walk.inReference) return null;
            const ids = (el.getAttribute("aria-labelledby") || "").trim();
            if (ids === "") return null;
            const contributions = [];
            for (const id of ids.split(/\s+/)) {
              const target = referencedElementOf(el, id);
              if (target === null) continue;
              contributions.push(contentNameOf(target, styleOf(target), {
                inReference: true,
                hiddenAllowed: isReferenceHidden(target),
                visited: walk.visited,
                accessible: walk.accessible,
              }));
            }
            if (contributions.join(" ") === "") return null;
            return contributions.map((text) => text.replace(/\s+/g, " ").trim()).filter((name) => name !== "");
          };
          const isSecureField = (el) => el instanceof HTMLInputElement && (el.type === "password" || el.getAttribute("autocomplete") === "current-password");
          // Every element under el and every element its aria-owns names, with theirs.
          const ariaOwnedOf = (el) => {
            const owned = Array.from(el.querySelectorAll("*"));
            for (const id of (el.getAttribute("aria-owns") || "").trim().split(/\s+/)) {
              const target = id === "" ? null : referencedElementOf(el, id);
              if (target !== null) owned.push(target, ...Array.from(target.querySelectorAll("*")));
            }
            return owned;
          };
          // What an embedded control contributes to a name computed through
          // another element (accname 2C): a text field its value, a combobox or
          // listbox its selected options, a range widget its value. Null for any
          // other element, and for one its own aria-labelledby names.
          const embeddedControlNameOf = (el, walk) => {
            const role = roleOf(el);
            if (role === null) return null;
            if (el.id !== "" && (el.getAttribute("aria-labelledby") || "").trim().split(/\s+/).indexOf(el.id) !== -1) return null;
            if (isSecureField(el)) return "";
            if (role === "textbox" || role === "searchbox") {
              return el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement ? el.value : el.textContent ?? "";
            }
            if (role === "combobox" || role === "listbox") {
              let selected;
              if (el instanceof HTMLSelectElement) {
                selected = Array.from(el.selectedOptions);
                if (selected.length === 0 && el.options.length > 0) selected.push(el.options[0]);
              } else {
                const listbox = role === "combobox" ? ariaOwnedOf(el).find((owned) => roleOf(owned) === "listbox") : el;
                selected = listbox === undefined
                  ? []
                  : ariaOwnedOf(listbox).filter((owned) => owned.getAttribute("aria-selected") === "true" && roleOf(owned) === "option");
              }
              if (selected.length === 0 && el instanceof HTMLInputElement) return el.value;
              return selected.map((option) => contentNameOf(option, styleOf(option), walk)).join(" ");
            }
            if (role === "progressbar" || role === "scrollbar" || role === "slider" || role === "spinbutton" || role === "meter") {
              if (el.hasAttribute("aria-valuetext")) return el.getAttribute("aria-valuetext") ?? "";
              if (el.hasAttribute("aria-valuenow")) return el.getAttribute("aria-valuenow") ?? "";
              return el.getAttribute("value") ?? "";
            }
            return role === "menu" ? "" : null;
          };
          // What one element contributes to a name computed through another, as
          // an aria-labelledby target (2B) or a descendant read for name from
          // content (2F): its references, its value as an embedded control, its
          // aria-label, its alt or svg title, its children, then its title.
          const contentNameOf = (el, style, walk) => {
            if (walk.visited.has(el)) return "";
            walk.visited.add(el);
            if (!walk.hiddenAllowed && isNameHidden(el, style)) return "";
            const referenced = referencedNamesOf(el, walk);
            if (referenced !== null) return referenced.join(" ");
            const embedded = walk.accessible ? embeddedControlNameOf(el, walk) : null;
            if (embedded !== null) return embedded;
            const ariaLabel = el.getAttribute("aria-label");
            if (ariaLabel !== null && ariaLabel.trim() !== "") return ariaLabel.trim();
            const alternative = altOf(el) ?? svgTitleOf(el);
            if (alternative !== null) return alternative;
            if (nameOpaqueTags.has(el.tagName)) return "";
            const content = childrenNameOf(el, walk);
            if (content !== "") return content;
            const title = el.getAttribute("title");
            return title === null ? "" : title.trim();
          };
          // The children's contributions joined as Playwright's role selector
          // joins them: a space on each side of a block-level child and of a
          // <br>, none around an inline one. An accessible name wraps them in
          // the element's generated content.
          const childrenNameOf = (el, walk) => {
            let out = walk.accessible ? generatedOf(el, "::before") : "";
            for (const child of contentChildrenOf(el)) {
              if (child.nodeType === 3) {
                out += child.nodeValue ?? "";
                continue;
              }
              if (!(child instanceof Element)) continue;
              const style = styleOf(child);
              const token = contentNameOf(child, style, walk);
              const block = child.tagName === "BR" || style.display !== "inline";
              out += block ? " " + token + " " : token;
            }
            return walk.accessible ? out + generatedOf(el, "::after") : out;
          };
          // A slot reads what is assigned to it; a host reads its light children
          // (a slotted one skipped, its slot reads it) and then its shadow tree.
          const contentChildrenOf = (el) => {
            if (el instanceof HTMLSlotElement) {
              const assigned = el.assignedNodes();
              if (assigned.length > 0) return assigned;
            }
            const shadow = shadowOf(el);
            if (shadow === null) return Array.from(el.childNodes);
            const slotted = new Set();
            for (const slot of Array.from(shadow.querySelectorAll("slot"))) {
              for (const node of slot.assignedNodes()) slotted.add(node);
            }
            const own = Array.from(el.childNodes).filter((child) => !slotted.has(child));
            return own.concat(Array.from(shadow.childNodes));
          };
          // Text for a name from an element's own content (accname 2F). named is
          // the element the text names when that is not el (a control read
          // through its <label>), which never contributes to its own name.
          const nameTextOf = (el, accessible, named = el) => {
            if (isNameHidden(el, styleOf(el))) return "";
            if (nameOpaqueTags.has(el.tagName)) return "";
            return childrenNameOf(el, nameWalk([el, named], accessible)).replace(/\s+/g, " ").trim();
          };
          const nameOpaqueTags = new Set(["TEXTAREA", "SELECT", "INPUT", "SCRIPT", "STYLE"]);
          // Roles named from their content (accname 2F, the list Playwright's
          // role selector uses), plus listitem, status, and alert.
          const nameFromContentRoles = new Set([
            "button", "cell", "checkbox", "columnheader", "gridcell", "heading", "link", "menuitem", "menuitemcheckbox",
            "menuitemradio", "option", "radio", "row", "rowheader", "switch", "tab", "tooltip", "treeitem", "listitem", "status", "alert"
          ]);
          // HTML-AAM: the child element that names its parent when nothing ARIA does.
          const namingChildTags = { FIELDSET: "LEGEND", FIGURE: "FIGCAPTION", TABLE: "CAPTION" };
          const placeholderNamedInputTypes = ["text", "password", "number", "search", "tel", "email", "url"];
          // The controls a placeholder may name: text-like inputs, textareas, and the textbox and searchbox roles.
          const isPlaceholderNamed = (el, role) => {
            if (el instanceof HTMLTextAreaElement) return true;
            if (el instanceof HTMLInputElement) return placeholderNamedInputTypes.indexOf(el.type) !== -1;
            return role === "textbox" || role === "searchbox";
          };
          const accessibleName = (el, role) => {
            // accname reads a labelledby reference (2B) before the element's own aria-label (2C).
            const referenced = referencedNamesOf(el, nameWalk([], true));
            if (referenced !== null) return referenced.join(" ");
            const ariaLabel = el.getAttribute("aria-label");
            if (ariaLabel !== null && ariaLabel.trim() !== "") return ariaLabel.trim();
            const labels = el.labels ? Array.from(el.labels) : [];
            if (labels.length > 0) {
              const joined = labels.map((label) => nameTextOf(label, true, el)).join(" ").trim();
              if (joined !== "") return joined;
            }
            const alternative = altOf(el) ?? (isPresentational(el) ? null : svgTitleOf(el));
            if (alternative !== null) return alternative;
            const captionTag = namingChildTags[el.tagName];
            if (captionTag !== undefined) {
              const caption = Array.from(el.children).find((child) => child.tagName === captionTag);
              const text = caption === undefined ? "" : nameTextOf(caption, true);
              if (text !== "") return text;
            }
            if (el instanceof HTMLInputElement && (el.type === "button" || el.type === "submit" || el.type === "reset")) {
              if (el.value.trim() !== "") return el.value.trim();
              // HTML-AAM: a submit or reset button with no value reads its default label.
              if (el.type === "submit") return "Submit";
              if (el.type === "reset") return "Reset";
            }
            if (role !== null && nameFromContentRoles.has(role)) {
              const text = nameTextOf(el, true);
              if (text !== "") return text;
            }
            const title = el.getAttribute("title");
            if (title !== null && title.trim() !== "") return title.trim();
            // HTML-AAM names an unlabeled text control by its placeholder, after the title.
            if (isPlaceholderNamed(el, role)) {
              const placeholder = el.getAttribute("placeholder");
              if (placeholder !== null && placeholder.trim() !== "") return placeholder.trim();
              const ariaPlaceholder = el.getAttribute("aria-placeholder");
              if (ariaPlaceholder !== null && ariaPlaceholder.trim() !== "") return ariaPlaceholder.trim();
            }
            return null;
          };
          // No name is null, as upstream reports it, so the snapshot shows the node's text instead.
          const nameOf = (el, role) => {
            const name = reportedName(el, accessibleName(el, role));
            return name === null ? null : cut(name, 256);
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
              if (attr.name === testIdAttribute) continue;
              out[attr.name] = attr.value;
            }
            return out;
          };
          // No layout box at all (display: none on it or an ancestor, any
          // display: contents element) is no rect, as Playwright's
          // boundingBox() answers null; a box with no size, or one
          // visibility: hidden keeps, is still a rect.
          const rectOf = (el) => {
            if (el.getClientRects().length === 0) return null;
            const box = el.getBoundingClientRect();
            return { x: box.x, y: box.y, width: box.width, height: box.height };
          };
          // Text owned directly by an element, not by its child elements. An
          // element with content-visibility: hidden renders none of it. The
          // budget tally and the walk both ask, so each answer is kept.
          const directTexts = new Map();
          const directTextOf = (el) => {
            if (directTexts.has(el)) return directTexts.get(el);
            let out = "";
            for (const child of el.childNodes) {
              if (child.nodeType === Node.TEXT_NODE) out += child.nodeValue ?? "";
            }
            out = out.replace(/\s+/g, " ").trim();
            if (out && getComputedStyle(el).getPropertyValue("content-visibility") === "hidden") out = "";
            directTexts.set(el, out);
            return out;
          };
          // The elements that reference each id from aria-labelledby, read once
          // per walk. nameOf resolves those ids in the document, so this does too.
          let labelledBy = null;
          const referencesOf = (id) => {
            if (!labelledBy) {
              labelledBy = new Map();
              for (const ref of document.querySelectorAll("[aria-labelledby]")) {
                for (const each of ref.getAttribute("aria-labelledby").trim().split(/\s+/)) {
                  if (!labelledBy.has(each)) labelledBy.set(each, []);
                  labelledBy.get(each).push(ref);
                }
              }
            }
            return labelledBy.get(id) ?? [];
          };
          // Text inside a label of a control, or in an element a role's
          // aria-labelledby names, is the labelled element's name already.
          const namesControl = (el) => {
            if (el.closest("label")?.control) return true;
            if (!el.id || document.getElementById(el.id) !== el) return false;
            return referencesOf(el.id).some((ref) => {
              const role = roleOf(ref);
              return !!role && role !== "presentation" && role !== "none";
            });
          };
          // As upstream's tree, an element is listed by its role, its test id,
          // or text of its own, so text inside a bare span or a strong is a node
          // a text query can answer with. Text that names another element is
          // not listed: that element carries it as its name, so a text query
          // answers with it, and a fill acts on the control a label names. An
          // SVG element has no innerText to read, so it is not listed by text.
          const ownsText = (el) => el instanceof HTMLElement && directTextOf(el) !== "" && !namesControl(el);
          const hasRole = (role) => !!role && role !== "presentation" && role !== "none";
          const listed = (el, role) => hasRole(role) || !!el.getAttribute(testIdAttribute) || ownsText(el);
          // A node listed for its text alone. It draws on its own allowance, so
          // a page full of text cannot push controls out of the snapshot.
          const textOnly = (el, role) => !hasRole(role) && !el.getAttribute(testIdAttribute);
          // How many nodes the walk would list, or only those on screen. Text-only nodes are not counted.
          const tally = (el, visible) => {
            if (!el || skip.has(el.tagName)) return 0;
            if (el.tagName === "IFRAME" && (ariaHidden(el) || hidden(el))) return 0;
            let n = 0;
            const role = roleOf(el);
            if (listed(el, role) && !textOnly(el, role)) {
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
          // A node under an ancestor that hides its subtree is hidden too.
          // parentHidden is that ancestor's verdict, not its own visibility.
          const walk = (el, into, parentHidden) => {
            if (!el || skip.has(el.tagName) || full) return;
            const style = getComputedStyle(el);
            const isHidden = !!parentHidden || ariaHidden(el) || hidden(el, style);
            const childrenHidden = !!parentHidden || hidesSubtree(el, style);
            const role = roleOf(el);
            const testId = el.getAttribute(testIdAttribute);
            const isFrame = el.tagName === "IFRAME";
            if (isFrame && isHidden) return;
            if (listed(el, role)) {
              const textNode = textOnly(el, role);
              if (textNode ? textCount >= max : count >= max) {
                if (!textNode) {
                  full = true;
                  return;
                }
                textCut = true;
                walkChildren(el, into, childrenHidden);
                return;
              }
              if (!textNode && offBudget < max && !inView(el)) {
                if (offCount >= offBudget) {
                  if ((role && leaves.has(role)) || el.tagName === "SELECT") return;
                  walkChildren(el, into, childrenHidden);
                  return;
                }
                offCount++;
              }
              if (textNode) textCount++;
              else count++;
              const type = (el.getAttribute("type") || "").toLowerCase();
              const secure = type === "password" || el.getAttribute("autocomplete") === "current-password";
              const ownsChildren = !(role && leaves.has(role)) && el.tagName !== "SELECT" && !isFrame;
              const node = {
                ref: stamp(el),
                role,
                name: isFrame ? cut(el.getAttribute("title") || "", 256) || null : nameOf(el, role),
                // A leaf keeps its content as text: a labelled status or button
                // reads its content, not its label, as upstream's node read
                // does. Any other node carries only its own direct text, since
                // the elements under it are listed with theirs. A secure field
                // withholds it. innerText is empty for a node that does not
                // render, and for a textarea; its DOM text is what a text query matches.
                text: secure ? "" : cut(ownsChildren ? directTextOf(el) : (isHidden || el.tagName === "TEXTAREA" ? el.textContent : el.innerText) || "", 512),
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
              walkChildren(el, node.children, childrenHidden);
              return;
            }
            walkChildren(el, into, childrenHidden);
          };
          // Light children first, then the shadow tree. Slotted elements are
          // light children and the shadow tree holds only their slots, so
          // nothing is listed twice.
          const walkChildren = (el, into, parentHidden) => {
            for (const child of el.children) walk(child, into, parentHidden);
            const shadow = shadowOf(el);
            if (shadow) for (const child of shadow.children) walk(child, into, parentHidden);
          };
          // innerText applies CSS text-transform, but locators match the DOM
          // text, as Playwright's do. The walk runs with text-transform
          // turned off, so an uppercase-styled "Net worth" reads as written.
          const roots = [];
          const sheets = document.adoptedStyleSheets;
          const plain = new CSSStyleSheet();
          plain.replaceSync("* { text-transform: none !important; }");
          document.adoptedStyleSheets = [...sheets, plain];
          let truncated;
          try {
            truncated = !!document.body && tally(document.body, false) > max;
            if (truncated) offBudget = Math.max(0, max - tally(document.body, true));
            if (document.body) walk(document.body, roots, false);
          } finally {
            document.adoptedStyleSheets = sheets;
          }
          window[Symbol.for("e2e.observation.elements")] = elements;
          return JSON.stringify({ next, count, truncated: truncated || full || textCut, scroll, roots });
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

    // Whether a checkable element is a radio, which a click can select but never clear.
    public const string IsRadio = """
        (node) => (node instanceof HTMLInputElement && node.type === "radio") || node.getAttribute("role") === "radio"
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

    // Whether a document ran RecordClosedShadowRoots.
    public const string TracksClosedShadowRoots = """
        () => globalThis[Symbol.for("e2e.closedShadowRoots")] instanceof WeakMap
        """;

    public const string Find = """
        (id) => {
          const el = window[Symbol.for("e2e.observation.elements")]?.get(id);
          return el && el.isConnected ? el : null;
        }
        """;
}
