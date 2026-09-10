import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import * as signalR from '@microsoft/signalr';
import { BrowserQRCodeReader, type IScannerControls } from '@zxing/browser';
import { ApiError, api, date, money, saveAuth, type Booking, type BookingItem, type CheckInResponse, type EventDetail, type EventItem, type Seat, type StageShape } from './api';
import { clearCheckoutSession, setActiveCheckout } from './checkout-session';
import { QRCodeSVG } from 'qrcode.react';

const shortDate = (value: string) => {
  const parsed = new Date(value);
  return {
    day: new Intl.DateTimeFormat('en-US', { day: '2-digit' }).format(parsed),
    month: new Intl.DateTimeFormat('en-US', { month: 'short' }).format(parsed).toUpperCase(),
  };
};

const salesAreOpen = (salesStartAt: string, salesEndAt: string, now = Date.now()) =>
  Date.parse(salesStartAt) <= now && now < Date.parse(salesEndAt);

const salesCountdown = (milliseconds: number) => {
  const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return [hours, minutes, seconds].map(value => String(value).padStart(2, '0')).join('.');
};

export function HomePage() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [now, setNow] = useState(Date.now());
  const [activeEventIndex, setActiveEventIndex] = useState(0);
  const heroRef = useRef<HTMLElement>(null);
  const listingRef = useRef<HTMLElement>(null);
  const query = useQuery({ queryKey: ['events', search, page], queryFn: () => api.events(search, page) });
  const pages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / query.data.pageSize)) : 1;
  const items = [...(query.data?.items.filter(event => Date.parse(event.endsAt) > now) ?? [])]
    .sort((left, right) => Date.parse(left.startsAt) - Date.parse(right.startsAt));

  useEffect(() => {
    setActiveEventIndex(0);
  }, [search, page]);

  const scrollToSection = (section: HTMLElement | null) => {
    if (!section) return;
    const headerHeight = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--header-height')) || 0;
    const top = section.getBoundingClientRect().top + window.scrollY - headerHeight;
    window.scrollTo({ top: Math.max(0, top), behavior: 'smooth' });
  };

  const revealEvents = () => scrollToSection(listingRef.current);
  const revealHero = () => scrollToSection(heroRef.current);

  useEffect(() => {
    let lastWheel = 0;
    const handleWheel = (event: WheelEvent) => {
      const currentTime = Date.now();
      if (Math.abs(event.deltaY) < 15) return;
      if (currentTime - lastWheel < 850) {
        event.preventDefault();
        return;
      }
      const listing = listingRef.current;
      if (!listing) return;
      const headerHeight = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--header-height')) || 0;
      const listingTop = Math.round(listing.getBoundingClientRect().top + window.scrollY - headerHeight);
      const currentY = window.scrollY;

      if (currentY < listingTop * 0.5 && event.deltaY > 0) {
        event.preventDefault();
        lastWheel = currentTime;
        revealEvents();
      } else if (currentY >= listingTop * 0.5 && event.deltaY < 0) {
        event.preventDefault();
        lastWheel = currentTime;
        revealHero();
      }
    };

    window.addEventListener('wheel', handleWheel, { passive: false });
    return () => window.removeEventListener('wheel', handleWheel);
  }, []);

  const moveEvents = (direction: -1 | 1) => {
    setActiveEventIndex(current => Math.min(Math.max(current + direction, 0), Math.max(items.length - 1, 0)));
  };

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const boundaries = query.data?.items
      .flatMap(event => [event.salesStartAt, event.salesEndAt, event.endsAt])
      .map(Date.parse)
      .filter(boundary => boundary > Date.now()) ?? [];
    const boundary = Math.min(...boundaries);
    if (!Number.isFinite(boundary)) return;
    const timer = window.setTimeout(() => {
      setNow(Date.now());
      void query.refetch();
    }, Math.min(boundary - Date.now(), 2_147_483_647));
    return () => window.clearTimeout(timer);
  }, [query.data?.items, query.refetch]);

  return <>
    <section className="hero" ref={heroRef} onClick={revealEvents}>
      <p className="kicker">LIVE INVENTORY / DIRECT BOOKING</p>
      <h1>Find your next<br />night out.</h1>
      <p>Browse upcoming events, choose exact seats, and keep every ticket in one place.</p>
      <button type="button" className="hero-reveal" onClick={event => { event.stopPropagation(); revealEvents(); }}>Explore events <span aria-hidden="true">↓</span></button>
      <span className="sr-only">Click, scroll, or use Explore events to browse the event list.</span>
    </section>

    <section className="listing-section" aria-labelledby="upcoming-events" ref={listingRef}>
      <div className="section-head">
        <div><p className="kicker">BOX OFFICE</p><h2 id="upcoming-events">Upcoming events</h2></div>
        <div className="feed-heading-tools">
          <span className="listing-count">{query.data?.totalCount ?? '—'} listed</span>
          {!query.isLoading && !query.isError && items.length > 0 && <span className="feed-position" aria-live="polite">{String(activeEventIndex + 1).padStart(2, '0')} / {String(items.length).padStart(2, '0')}</span>}
        </div>
      </div>
      <form className="search listing-search" onSubmit={event => event.preventDefault()}>
        <label htmlFor="search">Search the listings</label>
        <input id="search" type="search" placeholder="Event or venue" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
      </form>
      {query.isLoading
        ? <div className="event-board"><Skeleton /><Skeleton /><Skeleton /></div>
        : query.isError
          ? <ErrorState message="We couldn't load upcoming events." retry={() => query.refetch()} />
          : items.length === 0
            ? <p className="empty">No events match “{search}”. Try another event or venue.</p>
            : <>
              <p className="sr-only" id="event-feed-help">Use the previous and next buttons to browse one event at a time.</p>
              <div className="event-carousel" role="region" aria-label="Upcoming event carousel">
                <button type="button" className="feed-control feed-control-left" aria-controls="event-feed" aria-label="Previous event" disabled={activeEventIndex === 0} onClick={() => moveEvents(-1)}>←</button>
                <ul id="event-feed" className="event-feed" aria-label="Upcoming event cards" aria-describedby="event-feed-help">
                  {items.map((event, index) => {
                    const starts = shortDate(event.startsAt);
                    const bookingOpen = salesAreOpen(event.salesStartAt, event.salesEndAt, now);
                    const soldOut = event.availabilityStatus === 'SoldOut';
                    const salesOpeningIn = Date.parse(event.salesStartAt) - now;
                    return <li className={index === activeEventIndex ? 'is-active' : ''} key={event.id}>
                      <Link className="event-card" aria-label={`View ${event.name}`} aria-current={index === activeEventIndex ? 'true' : undefined} to={`/events/${event.id}`}>
                        <div className="event-card-media">
                          <img src={event.imageUrl} alt="" loading="lazy" decoding="async" />
                          <div className="event-card-wash" aria-hidden="true" />
                          <time className="event-card-date" dateTime={event.startsAt}><strong>{starts.day}</strong><span>{starts.month}</span></time>
                          {soldOut ? <span className="status soldout event-card-status">Sold out</span> : bookingOpen ? <span className="status published event-card-status">On sale</span> : salesOpeningIn > 0 ? <span className="sales-countdown event-card-status" role="timer" aria-label={`Tickets open in ${salesCountdown(salesOpeningIn)}`}>Tickets open in {salesCountdown(salesOpeningIn)}</span> : <span className="status draft event-card-status">Sales ended</span>}
                          <div className="event-card-caption">
                            <p className="event-card-eyebrow">{date(event.startsAt)}</p>
                            <h3>{event.name}</h3>
                            <p>{event.venueName}</p>
                          </div>
                        </div>
                        <div className="event-card-footer"><span>From</span><strong>{money(event.minPrice, event.currency)}</strong><span className="event-card-arrow" aria-hidden="true">↗</span></div>
                      </Link>
                    </li>;
                  })}
                </ul>
                <button type="button" className="feed-control feed-control-right" aria-controls="event-feed" aria-label="Next event" disabled={activeEventIndex === items.length - 1} onClick={() => moveEvents(1)}>→</button>
              </div>
            </>}
      {!query.isError && query.data && items.length > 0 && <nav className="pagination" aria-label="Event pages">
        <button className="ghost" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <span className="mono" aria-live="polite">Page {page} / {pages}</span>
        <button className="ghost" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Next</button>
      </nav>}
    </section>
  </>;
}

export function EventDetailPage() {
  const { id = '' } = useParams();
  const [now, setNow] = useState(Date.now());
  const query = useQuery({ queryKey: ['event', id], queryFn: () => api.event(id), refetchOnMount: 'always', refetchOnWindowFocus: true });
  const event = query.data;

  useEffect(() => {
    if (!event) return;
    const current = Date.now();
    const boundary = current < Date.parse(event.salesStartAt) ? Date.parse(event.salesStartAt) : Date.parse(event.salesEndAt);
    if (boundary <= current) return;
    const timer = window.setTimeout(() => setNow(Date.now()), Math.min(boundary - current, 2_147_483_647));
    return () => window.clearTimeout(timer);
  }, [event, now]);

  if (query.isLoading) return <Skeleton />;
  if (!event) return <ErrorState message="This event couldn't be loaded." retry={() => query.refetch()} />;

  const salesStart = Date.parse(event.salesStartAt);
  const salesEnd = Date.parse(event.salesEndAt);
  const eventEnd = Date.parse(event.endsAt);
  const salesOpen = salesStart <= now && now < salesEnd && now < eventEnd;
  const salesPending = now < salesStart;
  const soldOut = event.availabilityStatus === 'SoldOut';
  const minSeat = event.seats.reduce<Seat | undefined>((lowest, seat) => !lowest || seat.price < lowest.price ? seat : lowest, undefined);
  return <section className="detail">
    <div className="detail-hero">
      <div className="detail-media"><img src={event.imageUrl} alt="" /></div>
      <div className="detail-copy">
        <p className="kicker">{soldOut ? 'SOLD OUT' : salesPending ? 'SALES OPENING' : salesOpen ? 'NOW BOOKING' : 'SALES ENDED'}</p>
        <p className="mono">{date(event.startsAt)} – {date(event.endsAt)}</p>
        <h1>{event.name}</h1>
        <dl className="event-facts">
          <div><dt>Event time</dt><dd>{date(event.startsAt)} – {date(event.endsAt)}</dd></div>
          <div><dt>Ticket sales</dt><dd>{date(event.salesStartAt)} – {date(event.salesEndAt)}</dd></div>
          <div><dt>Venue</dt><dd>{event.venueName}</dd></div>
          <div><dt>Address</dt><dd>{event.address}</dd></div>
          <div><dt>Tickets</dt><dd>{minSeat ? `From ${money(minSeat.price, minSeat.currency)}` : 'Unavailable'}</dd></div>
        </dl>
        {salesOpen && !soldOut && <Link className="button" aria-label={`Choose seats for ${event.name}`} to={`/events/${event.id}/seats`}>Choose seats</Link>}
      </div>
    </div>
    <div className="detail-notes">
      <div><p className="kicker">ABOUT</p><h2>What to expect</h2><p>{event.description}</p></div>
      <aside><p className="kicker">TICKET SALES</p><h2>{soldOut ? 'Sold out' : salesPending ? 'Sales open' : salesOpen ? 'Book before' : 'Sales ended'}</h2><p className="mono">{date(soldOut ? event.endsAt : salesPending ? event.salesStartAt : event.salesEndAt)}</p><p>{soldOut ? 'All seats are currently unavailable.' : salesOpen ? 'Seat availability updates live while you browse.' : salesPending ? 'Booking will become available at the time shown above.' : 'This event remains available for reference.'}</p></aside>
    </div>
  </section>;
}

const authSchema = z.object({
  fullName: z.string().optional(),
  email: z.string().email('Enter a valid email address'),
  password: z.string().min(10, 'Password must contain at least 10 characters').regex(/[A-Z]/, 'Password must include an uppercase letter').regex(/[a-z]/, 'Password must include a lowercase letter').regex(/[0-9]/, 'Password must include a number').regex(/[^A-Za-z0-9]/, 'Password must include a special character'),
});
type AuthForm = z.infer<typeof authSchema>;

export function AuthPage() {
  const [register, setRegister] = useState(false);
  const [verifyEmail, setVerifyEmail] = useState<string | null>(null);
  const [verifyCode, setVerifyCode] = useState('');
  const [resendStatus, setResendStatus] = useState<string | null>(null);
  const nav = useNavigate();
  const location = useLocation();
  const qc = useQueryClient();
  const form = useForm<AuthForm>({ resolver: zodResolver(authSchema), defaultValues: { fullName: '', email: '', password: '' } });

  const loginMutation = useMutation({
    mutationFn: (value: AuthForm) => api.login(value.email, value.password),
    onSuccess: async response => {
      saveAuth(response);
      await qc.invalidateQueries({ queryKey: ['current-user'] });
      nav((location.state as { from?: string } | null)?.from ?? '/');
    },
    onError: (err: unknown, variables) => {
      if (err instanceof ApiError && (err.problem?.code === 'EMAIL_NOT_VERIFIED' || err.status === 403)) {
        setVerifyEmail(variables.email);
        setVerifyCode('');
        setResendStatus(null);
      }
    },
  });

  const registerMutation = useMutation({
    mutationFn: (value: AuthForm) => api.register(value.email, value.password, value.fullName!),
    onSuccess: (_, variables) => {
      setVerifyEmail(variables.email);
      setVerifyCode('');
      setResendStatus(null);
    },
  });

  const verifyMutation = useMutation({
    mutationFn: ({ email, code }: { email: string; code: string }) => api.verifyEmail(email, code),
    onSuccess: async response => {
      saveAuth(response);
      await qc.invalidateQueries({ queryKey: ['current-user'] });
      nav((location.state as { from?: string } | null)?.from ?? '/');
    },
  });

  const resendMutation = useMutation({
    mutationFn: (email: string) => api.resendVerification(email),
    onSuccess: () => {
      setResendStatus('A new verification code has been sent to your email.');
    },
  });

  const googleBtnRef = useRef<HTMLDivElement>(null);
  const googleMutation = useMutation({
    mutationFn: (idToken: string) => api.googleAuth(idToken),
    onSuccess: async response => {
      saveAuth(response);
      await qc.invalidateQueries({ queryKey: ['current-user'] });
      nav((location.state as { from?: string } | null)?.from ?? '/');
    },
  });

  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID;

  useEffect(() => {
    if (!googleClientId) return;

    const renderGoogle = () => {
      if (!window.google?.accounts?.id || !googleBtnRef.current) return false;
      window.google.accounts.id.initialize({
        client_id: googleClientId,
        callback: (res: { credential: string }) => {
          if (res.credential) {
            googleMutation.mutate(res.credential);
          }
        },
      });
      googleBtnRef.current.innerHTML = '';
      window.google.accounts.id.renderButton(googleBtnRef.current, {
        type: 'standard',
        theme: 'outline',
        size: 'large',
        text: register ? 'signup_with' : 'signin_with',
        shape: 'rectangular',
        width: 320,
      });
      return true;
    };

    if (!renderGoogle()) {
      const timer = setInterval(() => {
        if (renderGoogle()) clearInterval(timer);
      }, 200);
      return () => clearInterval(timer);
    }
  }, [googleClientId, register]);

  const submit = (value: AuthForm) => {
    if (register && (!value.fullName || value.fullName.trim().length < 2)) {
      form.setError('fullName', { message: 'Full name must contain at least 2 characters' });
      return;
    }
    if (register) {
      registerMutation.mutate(value);
    } else {
      loginMutation.mutate(value);
    }
  };
  const fillDemoAccount = (email: string, password: string) => {
    form.reset({ fullName: '', email, password });
    loginMutation.reset();
    registerMutation.reset();
    googleMutation.reset();
  };

  return <section className="auth">
    <div className="auth-intro"><p className="kicker">YOUR BOX OFFICE</p><h1>One account.<br />Every ticket.</h1><p>Book exact seats and find confirmed tickets whenever you need them.</p></div>
    <div className="auth-panel">
      {verifyEmail ? (
        <section aria-labelledby="verify-heading">
          <p className="kicker">VERIFY YOUR ACCOUNT</p>
          <h2 id="verify-heading">Check your email</h2>
          <p style={{ margin: '0 0 20px', color: 'var(--muted)', fontSize: '.88rem' }}>
            We sent a 6-digit verification code to <strong style={{ color: 'var(--ink-heading)' }}>{verifyEmail}</strong>. Enter it below to activate your account.
          </p>
          <form onSubmit={e => { e.preventDefault(); if (verifyCode.length === 6) verifyMutation.mutate({ email: verifyEmail, code: verifyCode }); }}>
            <div className="auth-field">
              <label>
                Verification code
                <input
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  className="auth-verify-code mono"
                  placeholder="123456"
                  value={verifyCode}
                  onChange={e => setVerifyCode(e.target.value.replace(/\D/g, ''))}
                  aria-invalid={verifyMutation.isError}
                  autoFocus
                />
              </label>
            </div>
            {verifyMutation.isError && <p className="error" role="alert">{verifyMutation.error.message}</p>}
            {resendStatus && <p className="success" role="status">{resendStatus}</p>}
            <button className="button" disabled={verifyMutation.isPending || verifyCode.length !== 6}>
              {verifyMutation.isPending ? 'Verifying…' : 'Verify email'}
            </button>
          </form>
          <div className="auth-verify-actions">
            <button
              type="button"
              className="ghost"
              disabled={resendMutation.isPending}
              onClick={() => { setResendStatus(null); resendMutation.mutate(verifyEmail); }}
            >
              {resendMutation.isPending ? 'Sending code…' : 'Resend code'}
            </button>
            <button
              type="button"
              className="text-button"
              onClick={() => { setVerifyEmail(null); setVerifyCode(''); setResendStatus(null); verifyMutation.reset(); }}
            >
              Back to sign in
            </button>
          </div>
        </section>
      ) : (
        <>
          <p className="kicker">{register ? 'NEW CUSTOMER' : 'WELCOME BACK'}</p>
          <h2>{register ? 'Create your account' : 'Sign in to continue'}</h2>
          {!register && <section className="demo-accounts" aria-labelledby="demo-accounts-heading">
            <h3 id="demo-accounts-heading">Demo accounts</h3>
            <button type="button" className="ghost" onClick={() => fillDemoAccount('demo@flashseat.dev', 'Demo@123456')}><strong>Customer</strong><span>demo@flashseat.dev / Demo@123456</span></button>
            <button type="button" className="ghost" onClick={() => fillDemoAccount('admin@flashseat.dev', 'Admin@123456')}><strong>Admin</strong><span>admin@flashseat.dev / Admin@123456</span></button>
          </section>}
          <form onSubmit={form.handleSubmit(submit)}>
            {register && <div className="auth-field"><label>Full name<input autoComplete="name" aria-invalid={!!form.formState.errors.fullName} aria-describedby={form.formState.errors.fullName ? 'full-name-error' : undefined} {...form.register('fullName')} /></label>{form.formState.errors.fullName && <small id="full-name-error">{form.formState.errors.fullName.message}</small>}</div>}
            <div className="auth-field"><label>Email<input type="email" autoComplete="email" aria-invalid={!!form.formState.errors.email} aria-describedby={form.formState.errors.email ? 'email-error' : undefined} {...form.register('email')} /></label>{form.formState.errors.email && <small id="email-error">{form.formState.errors.email.message}</small>}</div>
            <div className="auth-field"><label>Password<input type="password" autoComplete={register ? 'new-password' : 'current-password'} aria-invalid={!!form.formState.errors.password} aria-describedby={form.formState.errors.password ? 'password-error' : undefined} {...form.register('password')} /></label>{form.formState.errors.password && <small id="password-error">{form.formState.errors.password.message}</small>}</div>
            {((register ? registerMutation.error : loginMutation.error) as Error | null)?.message && <p className="error" role="alert">{((register ? registerMutation.error : loginMutation.error) as Error).message}</p>}
            <button className="button" disabled={register ? registerMutation.isPending : loginMutation.isPending}>{register ? (registerMutation.isPending ? 'Creating account…' : 'Create account') : (loginMutation.isPending ? 'Signing in…' : 'Sign in')}</button>
          </form>
          {googleClientId && <div className="auth-social">
            <div className="auth-divider"><span>OR</span></div>
            <div ref={googleBtnRef} className="google-btn-container" data-testid="google-signin-btn" />
            {googleMutation.isPending && <p className="mono">Authenticating with Google…</p>}
            {googleMutation.isError && <p className="error" role="alert">{googleMutation.error.message}</p>}
          </div>}
          <button className="text-button" onClick={() => { setRegister(!register); form.clearErrors(); loginMutation.reset(); registerMutation.reset(); googleMutation.reset(); }}>{register ? 'Already have an account? Sign in' : 'Need an account? Register'}</button>
        </>
      )}
    </div>
  </section>;
}

const groupSeats = (seats: Seat[]) => {
  const sections = new Map<string, Map<string, Seat[]>>();
  seats.forEach(seat => {
    if (!sections.has(seat.section)) sections.set(seat.section, new Map());
    const rows = sections.get(seat.section)!;
    if (!rows.has(seat.row)) rows.set(seat.row, []);
    rows.get(seat.row)!.push(seat);
  });
  return sections;
};

const stageClass = (shape: StageShape) => `visual-stage visual-stage-${shape.toLowerCase()}`;

const SECTION_COLORS = [
  '#ff7052', // Coral
  '#38bdf8', // Sky blue
  '#34d399', // Emerald
  '#c084fc', // Purple
  '#fbbf24', // Amber
  '#22d3ee', // Cyan
  '#f472b6', // Pink
  '#a78bfa', // Violet
];

function sectionColor(sections: string[], section: string): string {
  const idx = sections.indexOf(section);
  return idx < 0 ? SECTION_COLORS[0] : SECTION_COLORS[idx % SECTION_COLORS.length];
}

type SeatMapProps = {
  event: EventDetail;
  states: Map<string, string>;
  selected: string[];
  onToggle: (seat: Seat) => void;
};

function SeatMap({ event, states, selected, onToggle }: SeatMapProps) {
  const [hoveredSeat, setHoveredSeat] = useState<Seat | null>(null);
  const [highlightSection, setHighlightSection] = useState<string | null>(null);

  const sections = useMemo(() => {
    const map = new Map<string, { price: number; currency: string }>();
    event.seats.forEach(s => {
      if (!map.has(s.section)) {
        map.set(s.section, { price: s.price, currency: s.currency });
      }
    });
    return [...map.entries()].map(([name, info]) => ({ name, ...info }));
  }, [event.seats]);

  const sectionNames = useMemo(() => sections.map(s => s.name), [sections]);

  const hasLayout = event.seats.length > 0 && event.seats.every(seat =>
    typeof seat.layoutX === 'number' && Number.isFinite(seat.layoutX) &&
    typeof seat.layoutY === 'number' && Number.isFinite(seat.layoutY));
  const stageShape = event.stageShape ?? 'Proscenium';

  const renderSeat = (seat: Seat, visual = false) => {
    const state = states.get(seat.id) ?? 'Unavailable';
    const chosen = selected.includes(seat.id);
    const color = sectionColor(sectionNames, seat.section);
    const isDimmed = highlightSection && highlightSection !== seat.section;
    const isHovered = hoveredSeat?.id === seat.id;

    const visualStyle: React.CSSProperties = visual ? {
      left: `${seat.layoutX}%`,
      top: `${seat.layoutY}%`,
      ...(state === 'Available' ? {
        borderColor: isHovered || chosen ? color : `color-mix(in srgb, ${color} 75%, transparent)`,
        backgroundColor: chosen
          ? color
          : isHovered
            ? `color-mix(in srgb, ${color} 35%, var(--paper-light))`
            : `color-mix(in srgb, ${color} 18%, var(--paper-light))`,
        color: '#ffffff',
        boxShadow: chosen
          ? `0 0 14px ${color}`
          : isHovered
            ? `0 0 10px ${color}`
            : undefined,
        opacity: isDimmed ? 0.25 : 1,
        zIndex: isHovered ? 5 : chosen ? 3 : 2,
      } : {
        opacity: isDimmed ? 0.25 : 1,
      }),
    } : (state === 'Available' && chosen ? { backgroundColor: color, borderColor: color } : {});

    return <button
      key={seat.id}
      className={`seat ${visual ? 'visual-seat' : ''} ${state.toLowerCase()} ${chosen ? 'selected' : ''}`}
      style={visualStyle}
      disabled={state !== 'Available'}
      aria-pressed={chosen}
      aria-label={`Seat ${seat.row}${seat.number}, ${seat.section}, ${money(seat.price, seat.currency)}, ${chosen ? 'Selected' : state}`}
      title={`Hàng ${seat.row} - Ghế ${seat.number} (${seat.section}) · ${money(seat.price, seat.currency)}`}
      onMouseEnter={() => setHoveredSeat(seat)}
      onMouseLeave={() => setHoveredSeat(s => s?.id === seat.id ? null : s)}
      onFocus={() => setHoveredSeat(seat)}
      onBlur={() => setHoveredSeat(s => s?.id === seat.id ? null : s)}
      onClick={() => onToggle(seat)}
    >{seat.number}</button>;
  };

  return <div className={`seat-map ${hasLayout ? 'seat-map-visual' : ''}`}>
    {sections.length > 0 && (
      <div className="seat-tier-legend" aria-label="Bảng giá các hạng vé">
        <span className="tier-legend-title">Hạng vé & Giá:</span>
        <div className="tier-legend-items">
          {sections.map(sec => {
            const color = sectionColor(sectionNames, sec.name);
            const isHighlighted = highlightSection === sec.name;
            return (
              <button
                key={sec.name}
                type="button"
                className={`tier-legend-item ${isHighlighted ? 'active' : ''}`}
                onMouseEnter={() => setHighlightSection(sec.name)}
                onMouseLeave={() => setHighlightSection(null)}
                onClick={() => setHighlightSection(cur => cur === sec.name ? null : sec.name)}
                aria-pressed={isHighlighted}
              >
                <span className="tier-legend-swatch" style={{ backgroundColor: color, boxShadow: `0 0 8px ${color}` }} />
                <strong className="tier-legend-name">{sec.name}</strong>
                <span className="tier-legend-price">{money(sec.price, sec.currency)}</span>
              </button>
            );
          })}
        </div>
      </div>
    )}

    {hasLayout
      ? <div className={`visual-seat-canvas ${event.seats.length > 150 ? 'is-dense' : ''} ${event.seats.length > 600 ? 'is-ultra-dense' : ''}`} aria-label={`${stageShape} seating layout`}>
          <div className={stageClass(stageShape)} style={typeof event.stageX === 'number' && typeof event.stageY === 'number' ? { left: `${event.stageX}%`, top: `${event.stageY}%`, transform: 'translate(-50%, -50%)' } : undefined}><span>STAGE</span></div>
          {event.seats.map(seat => renderSeat(seat, true))}
          {hoveredSeat && typeof hoveredSeat.layoutX === 'number' && typeof hoveredSeat.layoutY === 'number' && (() => {
            const color = sectionColor(sectionNames, hoveredSeat.section);
            const state = states.get(hoveredSeat.id) ?? 'Unavailable';
            const isSelected = selected.includes(hoveredSeat.id);
            const isNearTop = hoveredSeat.layoutY < 24;
            return (
              <div
                className={`seat-tooltip ${isNearTop ? 'is-below' : 'is-above'}`}
                style={{
                  left: `${hoveredSeat.layoutX}%`,
                  top: `${hoveredSeat.layoutY}%`,
                }}
                role="tooltip"
              >
                <div className="seat-tooltip-top">
                  <strong className="seat-tooltip-title">Hàng {hoveredSeat.row} · Ghế {hoveredSeat.number}</strong>
                  <span className="seat-tooltip-badge" style={{ backgroundColor: color }}>
                    {hoveredSeat.section}
                  </span>
                </div>
                <div className="seat-tooltip-bottom">
                  <span className="seat-tooltip-price">{money(hoveredSeat.price, hoveredSeat.currency)}</span>
                  <span className={`seat-tooltip-status status-${state.toLowerCase()} ${isSelected ? 'status-selected' : ''}`}>
                    {isSelected ? 'Đang chọn' : state === 'Available' ? 'Có thể đặt' : state === 'Held' ? 'Đang giữ' : 'Đã bán'}
                  </span>
                </div>
              </div>
            );
          })()}
        </div>
      : <>
          <div className="stage"><span>STAGE</span></div>
          {[...groupSeats(event.seats)].map(([section, rows]) => <section className="seat-section" key={section}>
            <div className="seat-section-head"><h2>{section}</h2><span>{money([...rows.values()][0][0].price, [...rows.values()][0][0].currency)}</span></div>
            {[...rows].map(([row, seats]) => <div className="venue-row" key={row}>
              <span className="row-label">ROW {row}</span>
              <div className="seat-row-buttons">{seats.map(seat => renderSeat(seat))}</div>
            </div>)}
          </section>)}
        </>}
    <ul className="seat-legend" aria-label="Seat status legend">
      <li><i className="seat-swatch available" />Available</li><li><i className="seat-swatch selected" />Selected</li><li><i className="seat-swatch held" />Held</li><li><i className="seat-swatch booked" />Booked</li>
    </ul>
  </div>;
}

export function SeatPage() {
  const { id = '' } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [selected, setSelected] = useState<string[]>([]);
  const [reconciling, setReconciling] = useState(false);
  const event = useQuery({ queryKey: ['event', id], queryFn: () => api.event(id), refetchOnMount: 'always', refetchOnWindowFocus: true });
  const availability = useQuery({ queryKey: ['availability', id], queryFn: () => api.availability(id), refetchInterval: 15000, refetchOnMount: 'always', refetchOnWindowFocus: true });

  useEffect(() => {
    if (!availability.data) return;
    const available = new Set(availability.data.filter(item => item.status === 'Available').map(item => item.seatId));
    setSelected(items => items.filter(seatId => available.has(seatId)));
  }, [availability.data]);

  const refreshAvailability = useMemo(() => () => {
    qc.invalidateQueries({ queryKey: ['availability', id] });
    qc.invalidateQueries({ queryKey: ['event', id] });
    qc.invalidateQueries({ queryKey: ['events'] });
  }, [id, qc]);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/seat-availability', { accessTokenFactory: () => localStorage.getItem('accessToken') ?? '' }).withAutomaticReconnect().build();
    connection.on('SeatsHeld', refreshAvailability);
    connection.on('SeatsReleased', refreshAvailability);
    connection.on('SeatsBooked', refreshAvailability);
    connection.onreconnected(() => { connection.invoke('JoinEvent', id); refreshAvailability(); });
    connection.start().then(() => connection.invoke('JoinEvent', id)).catch(() => undefined);
    return () => { connection.invoke('LeaveEvent', id).catch(() => undefined).finally(() => connection.stop()); };
  }, [id, refreshAvailability]);

  const hold = useMutation({
    mutationFn: (seatIds: string[]) => api.createHold(id, seatIds),
    onSuccess: result => {
      qc.invalidateQueries({ queryKey: ['availability', id] });
      qc.invalidateQueries({ queryKey: ['event', id] });
      qc.invalidateQueries({ queryKey: ['events'] });
      setActiveCheckout({ holdId: result.id, eventId: id });
      nav(`/checkout/${result.id}`);
    },
    onError: async error => {
      if (!(error instanceof ApiError) || error.status !== 409) return;
      setReconciling(true);
      try {
        const latest = await availability.refetch();
        const available = new Set((latest.data ?? []).filter(item => item.status === 'Available').map(item => item.seatId));
        setSelected(items => items.filter(seatId => available.has(seatId)));
      } finally {
        setReconciling(false);
      }
    },
  });

  if (event.isLoading) return <Skeleton />;
  if (!event.data) return <ErrorState message="This seating plan couldn't be loaded." retry={() => event.refetch()} />;
  if (availability.isLoading) return <Skeleton label="Loading live seat availability" />;
  if (availability.isError) return <ErrorState message="Live seat availability is currently unavailable." retry={() => availability.refetch()} />;

  const availabilityItems = availability.data ?? [];
  const states = new Map(availabilityItems.map(item => [item.seatId, item.status]));
  const chosenSeats = event.data.seats.filter(seat => selected.includes(seat.id));
  const total = chosenSeats.reduce((sum, seat) => sum + seat.price, 0);
  const currency = chosenSeats[0]?.currency ?? event.data.seats[0]?.currency ?? 'VND';
  const holdError = hold.error instanceof ApiError ? hold.error : null;
  const unavailableSeats = holdError?.status === 409
    ? event.data.seats.filter(seat => holdError.problem.unavailableSeatIds.includes(seat.id)).map(seat => `${seat.section} ${seat.row}${seat.number}`)
    : [];
  const availableSeatCount = availabilityItems.filter(item => item.status === 'Available').length;
  const soldOut = availabilityItems.length > 0 && availableSeatCount === 0;

  return <section className="seat-page">
    <div className="page-heading"><p className="kicker">{soldOut ? 'SOLD OUT' : 'LIVE SEATING'}</p><h1>{event.data.name}</h1><p>{soldOut ? 'All seats are currently unavailable.' : 'Select up to 6 seats. Availability refreshes automatically.'}</p></div>
    <div className="seat-layout">
      <SeatMap
        event={event.data}
        states={states}
        selected={selected}
        onToggle={seat => {
          hold.reset();
          setSelected(items => items.includes(seat.id)
            ? items.filter(value => value !== seat.id)
            : items.length < 6 ? [...items, seat.id] : items);
        }}
      />
      <aside className="summary" aria-live="polite">
        <p className="kicker">YOUR ORDER</p><h2>{selected.length ? `${selected.length} seat${selected.length === 1 ? '' : 's'}` : 'No seats yet'}</h2>
        {chosenSeats.length > 0
          ? <ul className="chosen-seats">{chosenSeats.map(seat => <li key={seat.id}><span>{seat.section} · {seat.row}{seat.number}</span><strong>{money(seat.price, seat.currency)}</strong></li>)}</ul>
          : <p>Choose seats from the map to start your order.</p>}
        <div className="total"><span>Total</span><strong>{money(total, currency)}</strong></div>
        <button className="button" disabled={!selected.length || hold.isPending || reconciling || soldOut} onClick={() => hold.mutate([...selected])}>{reconciling ? 'Refreshing seats…' : hold.isPending ? 'Holding seats…' : 'Pay'}</button>
        <small>Maximum 6 seats per booking.</small>
        {soldOut && <p className="error" role="status">Sold out — no seats are currently available.</p>}
        {hold.isError && <p className="error" role="alert">{hold.error instanceof ApiError && hold.error.status === 409
          ? unavailableSeats.length ? `${unavailableSeats.join(', ')} ${unavailableSeats.length === 1 ? 'is' : 'are'} no longer available. Choose another seat.` : hold.error.problem.title
          : hold.error.message}</p>}
      </aside>
    </div>
  </section>;
}

const paymentKey = (holdId: string) => {
  const name = `flashseat:payment-key:${holdId}`;
  const existing = sessionStorage.getItem(name);
  if (existing) return existing;
  const key = crypto.randomUUID();
  sessionStorage.setItem(name, key);
  return key;
};

const secondsLeft = (expiresAt?: string) => expiresAt ? Math.max(0, Math.ceil((Date.parse(expiresAt) - Date.now()) / 1000)) : 0;
const countdown = (seconds: number) => `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;

export function CheckoutPage() {
  const { holdId = '' } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [remaining, setRemaining] = useState(0);
  const [bookingId, setBookingId] = useState(() => sessionStorage.getItem(`flashseat:booking-id:${holdId}`) ?? '');
  const [paymentId, setPaymentId] = useState(() => sessionStorage.getItem(`flashseat:payment-id:${holdId}`) ?? '');
  const initializedHold = useRef<string | null>(null);
  const expiredHold = useRef<string | null>(null);
  useEffect(() => {
    setBookingId(sessionStorage.getItem(`flashseat:booking-id:${holdId}`) ?? '');
    setPaymentId(sessionStorage.getItem(`flashseat:payment-id:${holdId}`) ?? '');
    initializedHold.current = null;
    expiredHold.current = null;
  }, [holdId]);
  const hold = useQuery({ queryKey: ['hold', holdId], queryFn: () => api.hold(holdId), refetchOnMount: 'always', refetchOnWindowFocus: true });
  const booking = useQuery({
    queryKey: ['booking', bookingId],
    queryFn: () => api.booking(bookingId),
    enabled: !!bookingId,
    refetchInterval: query => ['Confirmed', 'Cancelled', 'Expired'].includes(query.state.data?.status ?? '') ? false : 1500,
  });
  const paymentQuery = useQuery({
    queryKey: ['payment', paymentId],
    queryFn: () => api.payment(paymentId),
    enabled: !!paymentId,
    refetchInterval: query => ['Succeeded', 'Failed', 'Cancelled'].includes(query.state.data?.status ?? '') ? false : 2000,
  });
  const expired = hold.data ? secondsLeft(hold.data.expiresAt) === 0 || hold.data.status === 'Expired' || hold.data.status === 'Released' : false;

  useEffect(() => {
    if (!hold.data || expired || ['Confirmed', 'Cancelled', 'Expired'].includes(booking.data?.status ?? '')) return;
    setActiveCheckout({ holdId, eventId: hold.data.eventId });
  }, [booking.data?.status, expired, hold.data, holdId]);

  useEffect(() => {
    if (!hold.data || booking.data?.status === 'Confirmed') {
      if (booking.data?.status === 'Confirmed') setRemaining(0);
      return;
    }
    const update = () => {
      const left = secondsLeft(hold.data?.expiresAt);
      setRemaining(left);
      if (left <= 0 && expiredHold.current !== holdId) {
        expiredHold.current = holdId;
        api.releaseHold(holdId).catch(() => {});
        clearCheckoutSession(holdId);
        if (hold.data) qc.invalidateQueries({ queryKey: ['availability', hold.data.eventId] });
      }
    };
    update();
    const timer = window.setInterval(update, 1000);
    return () => window.clearInterval(timer);
  }, [hold.data, holdId, booking.data?.status, qc]);

  const pay = useMutation({
    mutationFn: async () => {
      let currentBookingId = bookingId;
      if (!currentBookingId) {
        const created = await api.createBooking(holdId);
        currentBookingId = created.id;
        setBookingId(created.id);
        sessionStorage.setItem(`flashseat:booking-id:${holdId}`, created.id);
        setActiveCheckout({ holdId, eventId: hold.data?.eventId ?? '' });
      }
      return api.createPayment(currentBookingId, paymentKey(holdId));
    },
    onSuccess: payment => {
      setPaymentId(payment.id);
      sessionStorage.setItem(`flashseat:payment-id:${holdId}`, payment.id);
      if (hold.data) setActiveCheckout({ holdId, eventId: hold.data.eventId });
    },
  });

  useEffect(() => {
    if (!hold.data || expired || booking.data?.status === 'Confirmed' || booking.data?.status === 'Cancelled' || booking.data?.status === 'Expired' || paymentId || initializedHold.current === holdId) return;
    initializedHold.current = holdId;
    pay.mutate();
  }, [hold.data, expired, booking.data?.status, paymentId, holdId, pay]);

  const release = useMutation({
    mutationFn: () => api.releaseHold(holdId),
    onSuccess: () => {
      clearCheckoutSession(holdId);
      if (hold.data) {
        qc.invalidateQueries({ queryKey: ['availability', hold.data.eventId] });
        qc.invalidateQueries({ queryKey: ['event', hold.data.eventId] });
        qc.invalidateQueries({ queryKey: ['events'] });
      }
      nav(hold.data ? `/events/${hold.data.eventId}/seats` : '/');
    },
  });

  const terminal = booking.data && ['Confirmed', 'Cancelled', 'Expired'].includes(booking.data.status);
  const confirmed = booking.data?.status === 'Confirmed';
  const activePayment = paymentQuery.data ?? pay.data;

  useEffect(() => {
    if (confirmed || booking.data?.status === 'Cancelled' || booking.data?.status === 'Expired' || ['Failed', 'Cancelled'].includes(paymentQuery.data?.status ?? '')) {
      clearCheckoutSession(holdId);
    }
  }, [booking.data?.status, confirmed, holdId, paymentQuery.data?.status]);

  useEffect(() => {
    if (!confirmed || !bookingId) return;
    clearCheckoutSession(holdId);
    qc.invalidateQueries({ queryKey: ['bookings'] });
    if (hold.data) {
      qc.invalidateQueries({ queryKey: ['availability', hold.data.eventId] });
      qc.invalidateQueries({ queryKey: ['event', hold.data.eventId] });
      qc.invalidateQueries({ queryKey: ['events'] });
    }
  }, [bookingId, confirmed, hold.data, holdId, qc]);

  if (hold.isLoading) return <Skeleton label="Loading held seats" />;
  if (hold.isError || !hold.data) return <ErrorState message="We couldn't load these held seats." retry={() => hold.refetch()} />;

  if (confirmed && bookingId) {
    return <section className="checkout-page payment-complete-page">
      <div className="payment-complete" role="status">
        <span className="payment-complete-icon" aria-hidden="true">✓</span>
        <p className="kicker">PAYMENT COMPLETE</p>
        <h1>Your booking is confirmed.</h1>
        <p>Your tickets are ready. Booking reference <strong className="mono">{booking.data?.bookingNumber}</strong>.</p>
        <div className="payment-complete-actions">
          <Link className="button" to={`/bookings/${bookingId}`}>View ticket detail</Link>
          <Link className="ghost" to={`/events/${hold.data.eventId}`}>Back to event</Link>
        </div>
      </div>
    </section>;
  }

  if (expired && !confirmed) {
    return (
      <section className="checkout-page">
        <div className="payment-result error" role="alert" style={{ maxWidth: '540px', margin: '60px auto', textAlign: 'center', padding: '40px 24px', borderRadius: '12px', background: 'var(--paper-light)', border: '1px solid var(--line)' }}>
          <div style={{ fontSize: '3rem', marginBottom: '16px' }}>⏰</div>
          <h2 style={{ fontSize: '1.6rem', margin: '0 0 12px', color: 'var(--ink-heading)' }}>Hết thời gian giữ chỗ</h2>
          <p style={{ margin: '0 0 28px', color: 'var(--muted)', fontSize: '1rem', lineHeight: '1.6' }}>
            Thời gian giữ ghế (5 phút) đã kết thúc. Ghế của bạn đã tự động được giải phóng về sơ đồ ghế để khán giả khác có thể đặt.
          </p>
          <Link
            className="button"
            to={`/events/${hold.data.eventId}/seats`}
            style={{ display: 'inline-flex', padding: '12px 28px', fontSize: '1rem' }}
            onClick={() => {
              clearCheckoutSession(holdId);
              qc.invalidateQueries({ queryKey: ['availability', hold.data.eventId] });
            }}
          >
            Back to seat selection
          </Link>
        </div>
      </section>
    );
  }

  return <section className="checkout-page">
    <div className="page-heading"><p className="kicker">SECURE CHECKOUT</p><h1>Complete payment.</h1><p>Your seats are held while the countdown is active.</p></div>
    <div className="checkout-layout">
      <div className="payment-card">
        <div className="checkout-status"><span className={`status ${expired ? 'expired' : confirmed ? 'confirmed' : 'pendingpayment'}`}>{expired ? 'Expired' : confirmed ? 'Paid' : 'Held'}</span><strong className="countdown" aria-label={`${remaining} seconds remaining`}>{countdown(remaining)}</strong></div>
        {activePayment?.qrCode && (
          <div className="vietqr-panel">
            <div className="vietqr-heading">
              <div>
                <p className="kicker">BANK TRANSFER</p>
                <h2>Quét mã để thanh toán</h2>
              </div>
              <span className="vietqr-badge">VietQR</span>
            </div>
            <div className="vietqr-code">
              <QRCodeSVG value={activePayment.qrCode} size={280} includeMargin aria-label="Bank transfer payment QR code" />
            </div>
            <dl className="transfer-details">
              <div><dt>Ngân hàng</dt><dd>{activePayment.bankId ?? '—'}</dd></div>
              <div><dt>Số tài khoản</dt><dd className="mono">{activePayment.accountNumber ?? '—'}</dd></div>
              <div><dt>Chủ tài khoản</dt><dd>{activePayment.accountName ?? '—'}</dd></div>
              <div><dt>Số tiền</dt><dd className="transfer-amount">{money(activePayment.amount, activePayment.currency)}</dd></div>
              <div><dt>Nội dung</dt><dd className="mono">{activePayment.transferDescription ?? `FS ${activePayment.orderCode ?? ''}`}</dd></div>
            </dl>
          </div>
        )}
        {!activePayment?.qrCode && !pay.isError && !confirmed && <p className="payment-loading" role="status">Đang tạo mã QR thanh toán PayOS…</p>}
        <dl className="payment-reference"><div><dt>Reference</dt><dd className="mono">{hold.data.id.slice(0, 8).toUpperCase()}</dd></div><div><dt>Due</dt><dd className="mono">{date(hold.data.expiresAt)}</dd></div></dl>
      </div>
      <aside className="summary" aria-live="polite">
        <p className="kicker">HELD SEATS</p><h2>{hold.data.items.length} seat{hold.data.items.length === 1 ? '' : 's'}</h2>
        <ul className="chosen-seats">{hold.data.items.map(seat => <li key={seat.seatId}><span>{seat.section} · {seat.row}{seat.number}</span><strong>{money(seat.price, hold.data.currency)}</strong></li>)}</ul>
        <div className="total"><span>Total</span><strong>{money(hold.data.totalAmount, hold.data.currency)}</strong></div>
        {!terminal && !expired && <button className="ghost" disabled={release.isPending || pay.isPending} onClick={() => release.mutate()}>{release.isPending ? 'Releasing seats…' : 'Back to seat selection'}</button>}
        {expired && !bookingId && <p className="error" role="alert">This hold expired. Return to the seat map and choose again.</p>}
        {(pay.isError || release.isError || booking.isError) && <p className="error" role="alert">{pay.error?.message ?? release.error?.message ?? "We couldn't confirm this booking yet."}</p>}
        {confirmed && <div className="payment-result" role="status"><strong>Payment confirmed.</strong><p style={{ margin: 0, fontSize: '.85rem', color: 'var(--muted)' }}>Vé điện tử đã được gửi tới email của bạn.</p><Link className="button" to="/bookings" onClick={() => { clearCheckoutSession(holdId); qc.invalidateQueries({ queryKey: ['bookings'] }); if (hold.data) { qc.invalidateQueries({ queryKey: ['availability', hold.data.eventId] }); qc.invalidateQueries({ queryKey: ['event', hold.data.eventId] }); qc.invalidateQueries({ queryKey: ['events'] }); } }}>View my tickets</Link></div>}
        {terminal && !confirmed && <div className="payment-result error" role="alert"><strong>Payment was not completed.</strong><Link className="ghost" to={`/events/${hold.data.eventId}/seats`}>Choose seats again</Link></div>}
      </aside>
    </div>
  </section>;
}

const bookingStatus = (status: string) => status.replace(/([a-z])([A-Z])/g, '$1 $2');

export function MyBookingsPage() {
  const query = useQuery({ queryKey: ['bookings'], queryFn: api.bookings, refetchOnMount: 'always', refetchOnWindowFocus: true });
  const user = useQuery({ queryKey: ['current-user'], queryFn: api.me });
  const confirmedBookings = query.data?.filter(booking => booking.status === 'Confirmed') ?? [];
  const totalSeats = confirmedBookings.reduce((sum, booking) => sum + booking.items.length, 0);

  return <section className="tickets-page">
    <div className="page-heading"><p className="kicker">YOUR BOX OFFICE</p><h1>My tickets</h1><p>Event details, booking references, and every seat in one place.</p></div>
    {user.data && <aside className="user-profile-card" aria-label="Account details">
      <div className="user-profile-avatar" aria-hidden="true">{user.data.fullName.slice(0, 2).toUpperCase()}</div>
      <div className="user-profile-info">
        <div className="user-profile-header">
          <h2>{user.data.fullName}</h2>
          <span className={`status ${user.data.role.toLowerCase()}`}>{user.data.role}</span>
        </div>
        <p className="user-profile-email mono">{user.data.email}</p>
      </div>
      <div className="user-profile-metrics">
        <div><span className="metric-num">{confirmedBookings.length}</span><span className="metric-label">Bookings</span></div>
        <div><span className="metric-num">{totalSeats}</span><span className="metric-label">Seats</span></div>
      </div>
    </aside>}
    {query.isLoading
      ? <Skeleton />
      : query.isError
        ? <ErrorState message="We couldn't load your tickets." retry={() => query.refetch()} />
        : confirmedBookings.length === 0
          ? <div className="empty"><p>You don't have any confirmed tickets yet.</p><Link className="button" to="/">Browse events</Link></div>
          : <div className="tickets">{confirmedBookings.map(booking => <article className="ticket ticket-summary" key={booking.id}>
            {booking.event?.imageUrl && <img className="ticket-event-image" src={booking.event.imageUrl} alt="" />}
            <div className="ticket-image-overlay" aria-hidden="true" />
            <div className="ticket-main">
              <span className={`status ${booking.status.toLowerCase()}`}>{bookingStatus(booking.status)}</span>
              <h2>{booking.event?.name ?? 'Event details unavailable'}</h2>
              <p className="ticket-number">{booking.bookingNumber}</p>
              {booking.event && <p>{booking.event.venueName} · {date(booking.event.startsAt)}</p>}
              <div className="ticket-seats">{booking.items.map(seat => <span key={seat.id ?? seat.seatId}>{seat.section} {seat.row}{seat.number}</span>)}</div>
              <Link className="button small" to={`/bookings/${booking.id}`}>View tickets</Link>
            </div>
            <div className="ticket-stub"><span>{booking.items.length} seat{booking.items.length === 1 ? '' : 's'}</span><strong>{money(booking.totalAmount, booking.currency)}</strong><small>FLASHSEAT</small></div>
          </article>)}</div>}
  </section>;
}

function TicketCard({ booking, ticket }: { booking: Booking; ticket: BookingItem }) {
  const confirmed = booking.status === 'Confirmed' && !!ticket.ticketCode;
  return <article className="ticket individual-ticket">
    <div className="ticket-main">
      <span className={`status ${ticket.checkInStatus?.toLowerCase() === 'checkedin' ? 'confirmed' : booking.status.toLowerCase()}`}>{ticket.checkInStatus === 'CheckedIn' ? 'Checked in' : bookingStatus(booking.status)}</span>
      <h2>{booking.event?.name ?? 'Event details unavailable'}</h2>
      <p className="ticket-number">{ticket.ticketCode || 'Ticket code pending'}</p>
      <p>{ticket.section} · {ticket.row}{ticket.number}</p>
      {booking.event && <p>{booking.event.venueName} · {date(booking.event.startsAt)}</p>}
      <p>{money(ticket.price, ticket.currency ?? booking.currency)}</p>
      {ticket.checkInStatus === 'CheckedIn' && ticket.checkedInAt && <p className="success">Checked in {date(ticket.checkedInAt)}</p>}
    </div>
    <div className="ticket-stub ticket-qr">{confirmed ? <QRCodeSVG value={`FS1:${ticket.ticketCode}`} size={170} includeMargin aria-label={`QR code for ticket ${ticket.ticketCode}`} /> : <span className="mono">QR available after payment</span>}<small>{ticket.ticketCode ? 'SCAN AT VENUE' : 'FLASHSEAT'}</small></div>
  </article>;
}

export function BookingDetailPage() {
  const { id = '' } = useParams();
  const query = useQuery({ queryKey: ['booking', id], queryFn: () => api.booking(id) });
  if (query.isLoading) return <Skeleton label="Loading tickets" />;
  if (query.isError || !query.data) return <ErrorState message="We couldn't load this booking." retry={() => query.refetch()} />;
  const booking = query.data;
  const confirmed = booking.status === 'Confirmed';
  return <section className="tickets-page">
    <div className="page-heading"><p className="kicker">BOOKING {booking.bookingNumber}</p><h1>{booking.event?.name ?? 'Your booking'}</h1><p>{booking.event?.venueName} · {booking.event ? date(booking.event.startsAt) : date(booking.createdAt)}</p></div>
    <div className="ticket-detail-actions"><Link className="ghost" to="/bookings">Back to my tickets</Link><span className={`status ${booking.status.toLowerCase()}`}>{bookingStatus(booking.status)}</span></div>
    {confirmed
      ? <div className="tickets">{booking.items.map(ticket => <TicketCard key={ticket.id ?? ticket.seatId} booking={booking} ticket={ticket} />)}</div>
      : <div className="empty" role="status"><p>This booking is not confirmed, so no ticket has been issued.</p><Link className="button" to={booking.event ? `/events/${booking.event.id}/seats` : '/'}>Return to events</Link></div>}
  </section>;
}

export function CheckInPage() {
  const [eventId, setEventId] = useState('');
  const [code, setCode] = useState('');
  const [scanning, setScanning] = useState(false);
  const [scannerMessage, setScannerMessage] = useState('Select an event before starting the camera.');
  const videoRef = useRef<HTMLVideoElement>(null);
  const controlsRef = useRef<IScannerControls>();
  const events = useQuery({ queryKey: ['check-in-events'], queryFn: () => api.events('', 1, 100) });
  const checkIn = useMutation({ mutationFn: ({ eventId: selectedEventId, code: ticketCode }: { eventId: string; code: string }) => api.checkIn(selectedEventId, ticketCode) });
  const selectedEvent = events.data?.items.find(event => event.id === eventId);
  const duplicateResponse = checkIn.error instanceof ApiError && checkIn.error.status === 409 &&
    typeof checkIn.error.problem.body === 'object' && checkIn.error.problem.body !== null &&
    'ticketCode' in checkIn.error.problem.body
    ? checkIn.error.problem.body as CheckInResponse
    : null;
  const mismatch = checkIn.error instanceof ApiError && checkIn.error.problem.code === 'ticket_event_mismatch';
  useEffect(() => () => { controlsRef.current?.stop(); }, []);
  useEffect(() => {
    controlsRef.current?.stop();
    setScanning(false);
    setCode('');
    checkIn.reset();
    setScannerMessage(eventId ? 'Camera scanner is off.' : 'Select an event before starting the camera.');
  }, [eventId]);
  useEffect(() => {
    if (!scanning || !eventId || !videoRef.current) return;
    let active = true;
    const selectedId = eventId;
    const reader = new BrowserQRCodeReader();
    setScannerMessage('Point the camera at a ticket QR code.');
    void reader.decodeFromConstraints({
      video: {
        facingMode: { ideal: 'environment' },
        width: { ideal: 1280 },
        height: { ideal: 720 },
      },
    }, videoRef.current, (result) => {
      if (!active || !result || checkIn.isPending || !selectedId) return;
      const value = result.getText().trim();
      if (!value) return;
      setCode(value);
      setScannerMessage('QR code read. Checking ticket…');
      setScanning(false);
      controlsRef.current?.stop();
      checkIn.mutate({ eventId: selectedId, code: value });
    }).then(controls => {
      if (active) controlsRef.current = controls;
      else controls.stop();
    }).catch(error => {
      if (!active) return;
      setScanning(false);
      setScannerMessage(error instanceof DOMException && error.name === 'NotAllowedError'
        ? 'Camera permission was denied. Enter the ticket code manually.'
        : 'Camera could not start. Enter the ticket code manually.');
    });
    return () => {
      active = false;
      controlsRef.current?.stop();
    };
  }, [scanning, eventId, checkIn.isPending]);
  const submit = (event: React.FormEvent) => { event.preventDefault(); if (eventId && code.trim()) checkIn.mutate({ eventId, code: code.trim() }); };
  return <section className="admin-page checkin-page">
    <div className="page-heading"><p className="kicker">VENUE OPERATIONS</p><h1>Check in tickets.</h1><p>Select the event you are operating before scanning or entering tickets.</p></div>
    <div className="checkin-event-selector">
      <label htmlFor="check-in-event">Event</label>
      {events.isLoading ? <p role="status">Loading events…</p> : events.isError ? <p className="error" role="alert">We couldn't load events. Try again.</p> : events.data?.items.length === 0 ? <p className="empty">No current events are available.</p> : <select id="check-in-event" value={eventId} disabled={checkIn.isPending} onChange={event => setEventId(event.target.value)}><option value="">Select an event</option>{events.data?.items.map((event: EventItem) => <option key={event.id} value={event.id}>{event.name} · {event.venueName} · {date(event.startsAt)}</option>)}</select>}
      {selectedEvent && <p className="selected-event" role="status"><strong>{selectedEvent.name}</strong> · {selectedEvent.venueName} · {date(selectedEvent.startsAt)}</p>}
    </div>
    <div className="checkin-layout">
      <div className="scanner-panel">
        {scanning ? <video ref={videoRef} className="scanner-video" aria-label="Ticket QR scanner" autoPlay muted playsInline /> : <div className="scanner-placeholder">{scannerMessage}</div>}
        <p className="scanner-status" role="status">{scannerMessage}</p>
        <button type="button" className="ghost" disabled={!eventId} onClick={() => { setScannerMessage('Starting camera…'); setScanning(value => !value); }}>{scanning ? 'Stop camera' : 'Scan with camera'}</button>
      </div>
      <div className="checkin-form-panel"><form onSubmit={submit}><label htmlFor="ticket-code">Ticket code<input id="ticket-code" value={code} onChange={event => setCode(event.target.value)} placeholder="FS1:..." autoComplete="off" disabled={!eventId} /></label><button className="button" disabled={!eventId || !code.trim() || checkIn.isPending}>{checkIn.isPending ? 'Checking…' : 'Check in ticket'}</button></form>{checkIn.isError && <div className="error" role="alert"><p>{mismatch ? 'This ticket belongs to another event.' : duplicateResponse ? 'This ticket was already checked in.' : checkIn.error.message}</p>{duplicateResponse && <p>{duplicateResponse.event?.name} · {duplicateResponse.ticket.section} {duplicateResponse.ticket.row}{duplicateResponse.ticket.number}{duplicateResponse.checkedInAt && ` · ${date(duplicateResponse.checkedInAt)}`}</p>}</div>}{checkIn.data && <div className="checkin-result" role="status"><strong>Ticket checked in.</strong><p>{checkIn.data.event?.name}</p><p>{checkIn.data.ticket.section} · {checkIn.data.ticket.row}{checkIn.data.ticket.number}</p><p>{checkIn.data.checkedInAt && date(checkIn.data.checkedInAt)}</p></div>}</div>
    </div>
  </section>;
}

function Skeleton({ label = 'Loading' }: { label?: string }) {
  return <div className="skeleton" role="status"><span className="sr-only">{label}</span></div>;
}

function ErrorState({ message, retry }: { message: string; retry: () => unknown }) {
  return <div className="empty" role="alert"><p>{message}</p><button className="ghost" onClick={retry}>Try again</button></div>;
}
