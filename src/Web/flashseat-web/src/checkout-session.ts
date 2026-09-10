export type ActiveCheckout = { holdId: string; eventId: string };

const activeCheckoutKey = 'flashseat:active-checkout';
const checkoutSessionChanged = 'flashseat-checkout-session-changed';

const notifyCheckoutSessionChanged = () => window.dispatchEvent(new Event(checkoutSessionChanged));

export const checkoutSessionEvent = checkoutSessionChanged;

export function readActiveCheckout(): ActiveCheckout | null {
  const value = sessionStorage.getItem(activeCheckoutKey);
  if (value) {
    try {
      const parsed: unknown = JSON.parse(value);
      if (typeof parsed === 'object' && parsed !== null && 'holdId' in parsed && 'eventId' in parsed && typeof parsed.holdId === 'string' && typeof parsed.eventId === 'string' && parsed.holdId) {
        return { holdId: parsed.holdId, eventId: parsed.eventId };
      }
    } catch {
      // Invalid client state is treated as no active checkout.
    }
    sessionStorage.removeItem(activeCheckoutKey);
  }

  for (let index = 0; index < sessionStorage.length; index += 1) {
    const key = sessionStorage.key(index);
    if (key?.startsWith('flashseat:booking-id:')) {
      const holdId = key.slice('flashseat:booking-id:'.length);
      if (holdId && sessionStorage.getItem(key)) return { holdId, eventId: '' };
    }
  }
  return null;
}

export function setActiveCheckout(checkout: ActiveCheckout) {
  sessionStorage.setItem(activeCheckoutKey, JSON.stringify(checkout));
  notifyCheckoutSessionChanged();
}

export function clearActiveCheckout(holdId?: string) {
  const active = readActiveCheckout();
  if (!active || (holdId && active.holdId !== holdId)) return;
  sessionStorage.removeItem(activeCheckoutKey);
  notifyCheckoutSessionChanged();
}

export function clearCheckoutSession(holdId: string) {
  sessionStorage.removeItem(`flashseat:booking-id:${holdId}`);
  sessionStorage.removeItem(`flashseat:payment-id:${holdId}`);
  sessionStorage.removeItem(`flashseat:payment-key:${holdId}`);
  clearActiveCheckout(holdId);
}
