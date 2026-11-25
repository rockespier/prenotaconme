/**
 * Browsers built into apps (Telegram, Instagram, Facebook, WhatsApp, ...): Google refuses to sign in there
 * ("disallowed_useragent"), so the page must be opened in Safari or Chrome.
 */
const IN_APP = /Telegram|FBAN|FBAV|FB_IAB|Instagram|WhatsApp|Line\/|MicroMessenger|Snapchat|musical_ly|TikTok|LinkedInApp|; wv\)/i;

export function isInAppBrowser(userAgent = globalThis.navigator?.userAgent ?? ''): boolean {
  if (IN_APP.test(userAgent)) return true;
  // iOS web views of other apps lack the "Safari/" token that Safari, Chrome (CriOS) and Firefox (FxiOS) send.
  return /iPhone|iPad|iPod/i.test(userAgent) && !/Safari\//i.test(userAgent);
}

export function isMobile(userAgent = globalThis.navigator?.userAgent ?? ''): boolean {
  return /Android|iPhone|iPad|iPod|Mobile/i.test(userAgent);
}

/**
 * Translation key for a failed Google sign-in. On phones the usual cause is a page opened from a chat or social
 * app (some only fail after the popup, so the user agent is not enough): suggest opening it in the browser.
 */
export function signInErrorKey(userAgent = globalThis.navigator?.userAgent ?? ''): string {
  return isInAppBrowser(userAgent) || isMobile(userAgent) ? 'login.errorInApp' : 'login.error';
}
