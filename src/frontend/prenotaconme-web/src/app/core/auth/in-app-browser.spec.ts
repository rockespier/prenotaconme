import { isInAppBrowser, signInErrorKey } from './in-app-browser';

const SAFARI_IOS =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1';
const WEBVIEW_IOS = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148';
const TELEGRAM_ANDROID =
  'Mozilla/5.0 (Linux; Android 14; Pixel 8; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/130.0 Mobile Safari/537.36 Telegram-Android/11.4.2';
const CHROME_DESKTOP = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36';

describe('in-app browser detection', () => {
  it('spots app web views but not real browsers', () => {
    expect(isInAppBrowser(WEBVIEW_IOS)).toBe(true);
    expect(isInAppBrowser(TELEGRAM_ANDROID)).toBe(true);
    expect(isInAppBrowser(SAFARI_IOS)).toBe(false);
    expect(isInAppBrowser(CHROME_DESKTOP)).toBe(false);
  });

  it('suggests opening the browser when sign-in fails on a phone', () => {
    expect(signInErrorKey(SAFARI_IOS)).toBe('login.errorInApp');
    expect(signInErrorKey(CHROME_DESKTOP)).toBe('login.error');
  });
});
