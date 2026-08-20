import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import * as signalR from '@microsoft/signalr';
import { BrowserQRCodeReader, type IScannerControls } from '@zxing/browser';
import { ApiError, api, date, money, saveAuth, type Booking, type BookingItem, type CheckInResponse, type Seat } from './api';
import { QRCodeSVG } from 'qrcode.react';
import demoPaymentQr from './assets/demo-payment-qr.svg';

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
  const query = useQuery({ queryKey: ['events', search, page], queryFn: () => api.events(search, page) });
  const pages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / query.data.pageSize)) : 1;
  const items = query.data?.items.filter(event => Date.parse(event.endsAt) > now) ?? [];

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
    <section className="hero">
      <p className="kicker">LIVE INVENTORY / DIRECT BOOKING</p>
      <h1>Find your next<br />night out.</h1>
      <p>Browse upcoming events, choose exact seats, and keep every ticket in one place.</p>
      <form className="search" onSubmit={event => event.preventDefault()}>
        <label htmlFor="search">Search the listings</label>
        <input id="search" type="search" placeholder="Event or venue" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
      </form>
    </section>

    <section className="listing-section" aria-labelledby="upcoming-events">
      <div className="section-head">
        <div><p className="kicker">BOX OFFICE</p><h2 id="upcoming-events">Upcoming events</h2></div>
        <span className="listing-count">{query.data?.totalCount ?? '—'} listed</span>
      </div>
      {query.isLoading
        ? <div className="event-board"><Skeleton /><Skeleton /><Skeleton /></div>
        : query.isError
          ? <ErrorState message="We couldn't load upcoming events." retry={() => query.refetch()} />
          : items.length === 0
            ? <p className="empty">No events match “{search}”. Try another event or venue.</p>
            : <div className="event-board">{items.map(event => {
              const starts = shortDate(event.startsAt);
              const bookingOpen = salesAreOpen(event.salesStartAt, event.salesEndAt, now);
              const soldOut = event.availabilityStatus === 'SoldOut';
              const salesOpeningIn = Date.parse(event.salesStartAt) - now;
              return <Link className="event-row" aria-label={`View ${event.name}`} to={`/events/${event.id}`} key={event.id}>
                <time className="date-block" dateTime={event.startsAt}><strong>{starts.day}</strong><span>{starts.month}</span></time>
                <div className="event-image"><img src={event.imageUrl} alt="" loading="lazy" decoding="async" /></div>
                <div className="event-copy"><h3>{event.name}</h3><p>{event.venueName}</p></div>
                <div className="event-action"><span>From</span><strong>{money(event.minPrice, event.currency)}</strong>{soldOut ? <span className="status soldout">Sold out</span> : bookingOpen ? <span className="status published">On sale</span> : salesOpeningIn > 0 ? <span className="sales-countdown" role="timer" aria-label={`Tickets open in ${salesCountdown(salesOpeningIn)}`}>Tickets open in {salesCountdown(salesOpeningIn)}</span> : <span className="status draft">Sales ended</span>}</div>
              </Link>;
            })}</div>}
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
  const query = useQuery({ queryKey: ['event', id], queryFn: () => api.event(id) });
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
  const nav = useNavigate();
  const location = useLocation();
  const qc = useQueryClient();
  const form = useForm<AuthForm>({ resolver: zodResolver(authSchema), defaultValues: { fullName: '', email: '', password: '' } });
  const mutation = useMutation({
    mutationFn: (value: AuthForm) => register ? api.register(value.email, value.password, value.fullName!) : api.login(value.email, value.password),
    onSuccess: async response => {
      saveAuth(response);
      await qc.invalidateQueries({ queryKey: ['current-user'] });
      nav((location.state as { from?: string } | null)?.from ?? '/');
    },
  });
  const submit = (value: AuthForm) => {
    if (register && (!value.fullName || value.fullName.trim().length < 2)) {
      form.setError('fullName', { message: 'Full name must contain at least 2 characters' });
      return;
    }
    mutation.mutate(value);
  };
  const fillDemoAccount = (email: string, password: string) => {
    form.reset({ fullName: '', email, password });
    mutation.reset();
  };

  return <section className="auth">
    <div className="auth-intro"><p className="kicker">YOUR BOX OFFICE</p><h1>One account.<br />Every ticket.</h1><p>Book exact seats and find confirmed tickets whenever you need them.</p></div>
    <div className="auth-panel">
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
        {mutation.isError && <p className="error" role="alert">{mutation.error.message}</p>}
        <button className="button" disabled={mutation.isPending}>{mutation.isPending ? (register ? 'Creating account…' : 'Signing in…') : register ? 'Create account' : 'Sign in'}</button>
      </form>
      <button className="text-button" onClick={() => { setRegister(!register); form.clearErrors(); mutation.reset(); }}>{register ? 'Already have an account? Sign in' : 'Need an account? Register'}</button>
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

export function SeatPage() {
  const { id = '' } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [selected, setSelected] = useState<string[]>([]);
  const [reconciling, setReconciling] = useState(false);
  const event = useQuery({ queryKey: ['event', id], queryFn: () => api.event(id) });
  const availability = useQuery({ queryKey: ['availability', id], queryFn: () => api.availability(id), refetchInterval: 15000 });

  useEffect(() => {
    const token = localStorage.getItem('accessToken');
    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/seat-availability', { accessTokenFactory: () => token ?? '' }).withAutomaticReconnect().build();
    connection.on('SeatsHeld', () => qc.invalidateQueries({ queryKey: ['availability', id] }));
    connection.on('SeatsReleased', () => qc.invalidateQueries({ queryKey: ['availability', id] }));
    connection.on('SeatsBooked', () => qc.invalidateQueries({ queryKey: ['availability', id] }));
    connection.onreconnected(() => { connection.invoke('JoinEvent', id); qc.invalidateQueries({ queryKey: ['availability', id] }); });
    connection.start().then(() => connection.invoke('JoinEvent', id)).catch(() => undefined);
    return () => { connection.invoke('LeaveEvent', id).catch(() => undefined).finally(() => connection.stop()); };
  }, [id, qc]);

  const hold = useMutation({
    mutationFn: (seatIds: string[]) => api.createHold(id, seatIds),
    onSuccess: result => {
      qc.invalidateQueries({ queryKey: ['availability', id] });
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
  const sections = groupSeats(event.data.seats);
  const holdError = hold.error instanceof ApiError ? hold.error : null;
  const unavailableSeats = holdError?.status === 409
    ? event.data.seats.filter(seat => holdError.problem.unavailableSeatIds.includes(seat.id)).map(seat => `${seat.section} ${seat.row}${seat.number}`)
    : [];
  const availableSeatCount = availabilityItems.filter(item => item.status === 'Available').length;
  const soldOut = availabilityItems.length > 0 && availableSeatCount === 0;

  return <section className="seat-page">
    <div className="page-heading"><p className="kicker">{soldOut ? 'SOLD OUT' : 'LIVE SEATING'}</p><h1>{event.data.name}</h1><p>{soldOut ? 'All seats are currently unavailable.' : 'Select up to 6 seats. Availability refreshes automatically.'}</p></div>
    <div className="seat-layout">
      <div className="seat-map">
        <div className="stage"><span>STAGE</span></div>
        <ul className="seat-legend" aria-label="Seat status legend">
          <li><i className="seat-swatch available" />Available</li><li><i className="seat-swatch selected" />Selected</li><li><i className="seat-swatch held" />Held</li><li><i className="seat-swatch booked" />Booked</li>
        </ul>
        {[...sections].map(([section, rows]) => <section className="seat-section" key={section}>
          <div className="seat-section-head"><h2>{section}</h2><span>{money([...rows.values()][0][0].price, [...rows.values()][0][0].currency)}</span></div>
          {[...rows].map(([row, seats]) => <div className="venue-row" key={row}>
            <span className="row-label">ROW {row}</span>
            <div className="seat-row-buttons">{seats.map(seat => {
              const state = states.get(seat.id) ?? 'Unavailable';
              const chosen = selected.includes(seat.id);
              return <button
                key={seat.id}
                className={`seat ${state.toLowerCase()} ${chosen ? 'selected' : ''}`}
                disabled={state !== 'Available'}
                aria-pressed={chosen}
                aria-label={`Seat ${seat.row}${seat.number}, ${seat.section}, ${money(seat.price, seat.currency)}, ${chosen ? 'Selected' : state}`}
                onClick={() => { hold.reset(); setSelected(items => chosen ? items.filter(value => value !== seat.id) : items.length < 6 ? [...items, seat.id] : items); }}
              >{seat.number}</button>;
            })}</div>
          </div>)}
        </section>)}
      </div>
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
  const [bookingId, setBookingId] = useState('');
  const hold = useQuery({ queryKey: ['hold', holdId], queryFn: () => api.hold(holdId) });
  const booking = useQuery({
    queryKey: ['booking', bookingId],
    queryFn: () => api.booking(bookingId),
    enabled: !!bookingId,
    refetchInterval: query => ['Confirmed', 'Cancelled', 'Expired'].includes(query.state.data?.status ?? '') ? false : 1000,
  });

  useEffect(() => {
    if (!hold.data) return;
    const update = () => setRemaining(secondsLeft(hold.data?.expiresAt));
    update();
    const timer = window.setInterval(update, 1000);
    return () => window.clearInterval(timer);
  }, [hold.data]);

  const pay = useMutation({
    mutationFn: async (result: 'Success' | 'Failed') => {
      const created = await api.createBooking(holdId);
      setBookingId(created.id);
      return api.createPayment(created.id, result, paymentKey(holdId));
    },
  });
  const release = useMutation({
    mutationFn: () => api.releaseHold(holdId),
    onSuccess: () => {
      if (hold.data) qc.invalidateQueries({ queryKey: ['availability', hold.data.eventId] });
      nav(hold.data ? `/events/${hold.data.eventId}/seats` : '/');
    },
  });

  if (hold.isLoading) return <Skeleton label="Loading held seats" />;
  if (hold.isError || !hold.data) return <ErrorState message="We couldn't load these held seats." retry={() => hold.refetch()} />;

  const expired = secondsLeft(hold.data.expiresAt) === 0 || hold.data.status === 'Expired' || hold.data.status === 'Released';
  const terminal = booking.data && ['Confirmed', 'Cancelled', 'Expired'].includes(booking.data.status);
  const confirmed = booking.data?.status === 'Confirmed';

  return <section className="checkout-page">
    <div className="page-heading"><p className="kicker">SECURE CHECKOUT / DEMO</p><h1>Complete payment.</h1><p>Your seats are held while the countdown is active.</p></div>
    <div className="checkout-layout">
      <div className="payment-card">
        <div className="checkout-status"><span className={`status ${expired ? 'expired' : 'pendingpayment'}`}>{expired ? 'Expired' : 'Held'}</span><strong className="countdown" aria-label={`${remaining} seconds remaining`}>{countdown(remaining)}</strong></div>
        <img className="demo-qr" src={demoPaymentQr} alt="FlashSeat demo payment QR; no real payment" />
        <p className="demo-warning" role="note"><strong>Demo payment</strong> — no real money is transferred and this QR does not connect to a bank.</p>
        <dl className="payment-reference"><div><dt>Reference</dt><dd className="mono">{hold.data.id.slice(0, 8).toUpperCase()}</dd></div><div><dt>Due</dt><dd className="mono">{date(hold.data.expiresAt)}</dd></div></dl>
      </div>
      <aside className="summary" aria-live="polite">
        <p className="kicker">HELD SEATS</p><h2>{hold.data.items.length} seat{hold.data.items.length === 1 ? '' : 's'}</h2>
        <ul className="chosen-seats">{hold.data.items.map(seat => <li key={seat.seatId}><span>{seat.section} · {seat.row}{seat.number}</span><strong>{money(seat.price, hold.data.currency)}</strong></li>)}</ul>
        <div className="total"><span>Total</span><strong>{money(hold.data.totalAmount, hold.data.currency)}</strong></div>
        {!terminal && <button className="button" disabled={expired || pay.isPending || !!pay.data} onClick={() => pay.mutate('Success')}>{pay.isPending ? 'Submitting payment…' : pay.data ? 'Waiting for confirmation…' : 'Confirm demo payment'}</button>}
        {!pay.data && !terminal && <button className="ghost" disabled={release.isPending || pay.isPending || expired} onClick={() => release.mutate()}>{release.isPending ? 'Releasing…' : 'Release seats'}</button>}
        {!pay.data && !terminal && <details className="demo-controls"><summary>Demo controls</summary><button className="danger" disabled={expired || pay.isPending} onClick={() => pay.mutate('Failed')}>Simulate failed payment</button></details>}
        {expired && !bookingId && <p className="error" role="alert">This hold expired. Return to the seat map and choose again.</p>}
        {(pay.isError || release.isError || booking.isError) && <p className="error" role="alert">{pay.error?.message ?? release.error?.message ?? "We couldn't confirm this booking yet."}</p>}
        {bookingId && !terminal && !booking.isError && <p role="status">Payment submitted. Waiting for booking confirmation…</p>}
        {confirmed && <div className="payment-result" role="status"><strong>Payment confirmed.</strong><Link className="button" to="/bookings" onClick={() => { sessionStorage.removeItem(`flashseat:payment-key:${holdId}`); qc.invalidateQueries({ queryKey: ['bookings'] }); }}>View my tickets</Link></div>}
        {terminal && !confirmed && <div className="payment-result error" role="alert"><strong>Payment was not completed.</strong><Link className="ghost" to={`/events/${hold.data.eventId}/seats`}>Choose seats again</Link></div>}
      </aside>
    </div>
  </section>;
}

const bookingStatus = (status: string) => status.replace(/([a-z])([A-Z])/g, '$1 $2');

export function MyBookingsPage() {
  const query = useQuery({ queryKey: ['bookings'], queryFn: api.bookings });
  return <section className="tickets-page">
    <div className="page-heading"><p className="kicker">YOUR BOX OFFICE</p><h1>My tickets</h1><p>Event details, booking references, and every seat in one place.</p></div>
    {query.isLoading
      ? <Skeleton />
      : query.isError
        ? <ErrorState message="We couldn't load your tickets." retry={() => query.refetch()} />
        : query.data?.length === 0
          ? <div className="empty"><p>You don't have any tickets yet.</p><Link className="button" to="/">Browse events</Link></div>
          : <div className="tickets">{query.data?.map(booking => <article className="ticket" key={booking.id}>
            <div className="ticket-main">
              {booking.event?.imageUrl && <img className="ticket-event-image" src={booking.event.imageUrl} alt="" />}
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
  return <section className="tickets-page">
    <div className="page-heading"><p className="kicker">BOOKING {booking.bookingNumber}</p><h1>{booking.event?.name ?? 'Your tickets'}</h1><p>{booking.event?.venueName} · {booking.event ? date(booking.event.startsAt) : date(booking.createdAt)}</p></div>
    <div className="ticket-detail-actions"><Link className="ghost" to="/bookings">Back to my tickets</Link><span className={`status ${booking.status.toLowerCase()}`}>{bookingStatus(booking.status)}</span></div>
    <div className="tickets">{booking.items.map(ticket => <TicketCard key={ticket.id ?? ticket.seatId} booking={booking} ticket={ticket} />)}</div>
  </section>;
}

export function CheckInPage() {
  const [code, setCode] = useState('');
  const [scanning, setScanning] = useState(false);
  const [scannerMessage, setScannerMessage] = useState('Camera scanner is off.');
  const videoRef = useRef<HTMLVideoElement>(null);
  const controlsRef = useRef<IScannerControls>();
  const checkIn = useMutation({ mutationFn: (value: string) => api.checkIn(value) });
  const duplicateResponse = checkIn.error instanceof ApiError && checkIn.error.status === 409 &&
    typeof checkIn.error.problem.body === 'object' && checkIn.error.problem.body !== null &&
    'ticketCode' in checkIn.error.problem.body
    ? checkIn.error.problem.body as CheckInResponse
    : null;
  useEffect(() => () => { controlsRef.current?.stop(); }, []);
  useEffect(() => {
    if (!scanning || !videoRef.current) return;
    let active = true;
    const reader = new BrowserQRCodeReader();
    setScannerMessage('Point the camera at a ticket QR code.');
    void reader.decodeFromConstraints({
      video: {
        facingMode: { ideal: 'environment' },
        width: { ideal: 1280 },
        height: { ideal: 720 },
      },
    }, videoRef.current, (result) => {
      if (!active || !result || checkIn.isPending) return;
      const value = result.getText().trim();
      if (!value) return;
      setCode(value);
      setScannerMessage('QR code read. Checking ticket…');
      setScanning(false);
      controlsRef.current?.stop();
      checkIn.mutate(value);
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
  }, [scanning, checkIn.isPending]);
  const submit = (event: React.FormEvent) => { event.preventDefault(); if (code.trim()) checkIn.mutate(code.trim()); };
  return <section className="admin-page checkin-page">
    <div className="page-heading"><p className="kicker">VENUE OPERATIONS</p><h1>Check in tickets.</h1><p>Scan each ticket once, or enter its code manually.</p></div>
    <div className="checkin-layout">
      <div className="scanner-panel">
        {scanning ? <video ref={videoRef} className="scanner-video" aria-label="Ticket QR scanner" autoPlay muted playsInline /> : <div className="scanner-placeholder">{scannerMessage}</div>}
        <p className="scanner-status" role="status">{scannerMessage}</p>
        <button className="ghost" onClick={() => { setScannerMessage('Starting camera…'); setScanning(value => !value); }}>{scanning ? 'Stop camera' : 'Scan with camera'}</button>
      </div>
      <div className="checkin-form-panel"><form onSubmit={submit}><label htmlFor="ticket-code">Ticket code<input id="ticket-code" value={code} onChange={event => setCode(event.target.value)} placeholder="FS1:..." autoComplete="off" /></label><button className="button" disabled={!code.trim() || checkIn.isPending}>{checkIn.isPending ? 'Checking…' : 'Check in ticket'}</button></form>{checkIn.isError && <div className="error" role="alert"><p>{duplicateResponse ? 'This ticket was already checked in.' : checkIn.error.message}</p>{duplicateResponse && <p>{duplicateResponse.event?.name} · {duplicateResponse.ticket.section} {duplicateResponse.ticket.row}{duplicateResponse.ticket.number}{duplicateResponse.checkedInAt && ` · ${date(duplicateResponse.checkedInAt)}`}</p>}</div>}{checkIn.data && <div className="checkin-result" role="status"><strong>Ticket checked in.</strong><p>{checkIn.data.event?.name}</p><p>{checkIn.data.ticket.section} · {checkIn.data.ticket.row}{checkIn.data.ticket.number}</p><p>{checkIn.data.checkedInAt && date(checkIn.data.checkedInAt)}</p></div>}</div>
    </div>
  </section>;
}

function Skeleton({ label = 'Loading' }: { label?: string }) {
  return <div className="skeleton" role="status"><span className="sr-only">{label}</span></div>;
}

function ErrorState({ message, retry }: { message: string; retry: () => unknown }) {
  return <div className="empty" role="alert"><p>{message}</p><button className="ghost" onClick={retry}>Try again</button></div>;
}
