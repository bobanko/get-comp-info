// Tiny DOM helpers: h('div', { class: 'x', onclick }, child, ...) builds elements.

export function h(tag, props = {}, ...children) {
  const element = document.createElement(tag);
  for (const [name, value] of Object.entries(props ?? {})) {
    if (value === undefined || value === null || value === false) continue;
    if (name === 'class') element.className = value;
    else if (name === 'value') element.value = value;
    else if (name === 'dataset') Object.assign(element.dataset, value);
    else if (name.startsWith('on') && typeof value === 'function') element.addEventListener(name.slice(2), value);
    else if (name in element && typeof value !== 'string') element[name] = value;
    else element.setAttribute(name, value === true ? '' : value);
  }
  append(element, children);
  return element;
}

function append(parent, children) {
  for (const child of children.flat(Infinity)) {
    if (child === null || child === undefined || child === false) continue;
    parent.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

export function replace(parent, ...children) {
  parent.replaceChildren();
  append(parent, children);
}

let toastTimer = null;

export function toast(message, kind = 'ok') {
  const element = document.getElementById('toast');
  element.textContent = message;
  element.className = `toast toast-${kind}`;
  element.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    element.hidden = true;
  }, kind === 'error' ? 8000 : 3500);
}

export function errorBox(error, onRetry) {
  const expired = error?.code === 'session_expired';
  return h(
    'div',
    { class: 'notice notice-error', role: 'alert' },
    h('p', {}, error?.message ?? String(error)),
    expired
      ? h('button', { class: 'button', onclick: () => location.reload() }, 'Обновить страницу')
      : onRetry && h('button', { class: 'button', onclick: onRetry }, 'Повторить'),
  );
}

export function readSetting(key, fallback) {
  try {
    const value = localStorage.getItem(key);
    return value === null ? fallback : JSON.parse(value);
  } catch {
    return fallback;
  }
}

export function writeSetting(key, value) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Private mode or blocked storage: settings simply are not remembered.
  }
}

export function downloadText(fileName, text, type) {
  // UTF-8 BOM so Excel opens Cyrillic correctly, like the console export.
  const blob = new Blob(['﻿', text], { type });
  const url = URL.createObjectURL(blob);
  const link = h('a', { href: url, download: fileName });
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}
