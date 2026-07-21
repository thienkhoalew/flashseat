import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import * as signalR from '@microsoft/signalr';
import { ApiError, api, date, money, saveAuth, type Seat } from './api';
import demoPaymentQr from './assets/demo-payment-qr.svg';

const shortDate = (value: string) => {
  const parsed = new Date(value);
  return {
    day: new Intl.DateTimeFormat('en-US', { day: '2-digit' }).format(parsed),
    month: new Intl.DateTimeFormat('en-US', { month: 'short' }).format(parsed).toUpperCase(),
  };
};

export function HomePage() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const query = useQuery({ queryKey: ['events', search, page], queryFn: () => api.events(search, page) });
  const pages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / query.data.pageSize)) : 1;

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
          : query.data?.items.length === 0
            ? <p className="empty">No events match “{search}”. Try another event or venue.</p>
            : <div className="event-board">{query.data?.items.map(event => {
              const starts = shortDate(event.startsAt);
              return <article className="event-row" key={event.id}>
                <time className="date-block" dateTime={event.startsAt}><strong>{starts.day}</strong><span>{starts.month}</span></time>
                <img src={event.imageUrl} alt="" loading="lazy" decoding="async" />
                <div className="event-copy"><p className="mono">{date(event.startsAt)}</p><h3>{event.name}</h3><p>{event.venueName}</p></div>
                <div className="event-action"><span>From</span><strong>{money(event.minPrice, event.currency)}</strong><Link aria-label={`View ${event.name}`} to={`/events/${event.id}`}>View event</Link></div>
              </article>;
            })}</div>}
      {!query.isError && query.data && query.data.items.length > 0 && <nav className="pagination" aria-label="Event pages">
        <button className="ghost" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <span className="mono" aria-live="polite">Page {page} / {pages}</span>
        <button className="ghost" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Next</button>
      </nav>}
    </section>
  </>;
}

export function EventDetailPage() {
  const { id = '' } = useParams();
  const query = useQuery({ queryKey: ['event', id], queryFn: () => api.event(id) });
  if (query.isLoading) return <Skeleton />;
  if (!query.data) return <ErrorState message="This event couldn't be loaded." retry={() => query.refetch()} />;

  const event = query.data;
  const minSeat = event.seats.reduce<Seat | undefined>((lowest, seat) => !lowest || seat.price < lowest.price ? seat : lowest, undefined);
  return <section className="detail">
    <div className="detail-hero">
      <div className="detail-media"><img src={event.imageUrl} alt="" /></div>
      <div className="detail-copy">
        <p className="kicker">NOW BOOKING</p>
        <p className="mono">{date(event.startsAt)}</p>
        <h1>{event.name}</h1>
        <dl className="event-facts">
          <div><dt>Venue</dt><dd>{event.venueName}</dd></div>
          <div><dt>Address</dt><dd>{event.address}</dd></div>
          <div><dt>Tickets</dt><dd>{minSeat ? `From ${money(minSeat.price, minSeat.currency)}` : 'Unavailable'}</dd></div>
        </dl>
        <Link className="button" to={`/events/${event.id}/seats`}>Choose seats</Link>
      </div>
    </div>
    <div className="detail-notes">
      <div><p className="kicker">ABOUT</p><h2>What to expect</h2><p>{event.description}</p></div>
      <aside><p className="kicker">TICKET SALES</p><h2>Book before</h2><p className="mono">{date(event.salesEndAt)}</p><p>Seat availability updates live while you browse.</p></aside>
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
    onError: error => {
      availability.refetch();
      if (error instanceof ApiError && error.status === 409)
        setSelected(items => items.filter(seatId => !error.problem.unavailableSeatIds.includes(seatId)));
    },
  });

  if (event.isLoading) return <Skeleton />;
  if (!event.data) return <ErrorState message="This seating plan couldn't be loaded." retry={() => event.refetch()} />;
  if (availability.isLoading) return <Skeleton label="Loading live seat availability" />;
  if (availability.isError) return <ErrorState message="Live seat availability is currently unavailable." retry={() => availability.refetch()} />;

  const states = new Map(availability.data?.map(item => [item.seatId, item.status]));
  const chosenSeats = event.data.seats.filter(seat => selected.includes(seat.id));
  const total = chosenSeats.reduce((sum, seat) => sum + seat.price, 0);
  const currency = chosenSeats[0]?.currency ?? event.data.seats[0]?.currency ?? 'VND';
  const sections = groupSeats(event.data.seats);
  const holdError = hold.error instanceof ApiError ? hold.error : null;
  const unavailableSeats = holdError?.status === 409
    ? event.data.seats.filter(seat => holdError.problem.unavailableSeatIds.includes(seat.id)).map(seat => `${seat.section} ${seat.row}${seat.number}`)
    : [];

  return <section className="seat-page">
    <div className="page-heading"><p className="kicker">LIVE SEATING</p><h1>{event.data.name}</h1><p>Select up to 6 seats. Availability refreshes automatically.</p></div>
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
        <button className="button" disabled={!selected.length || hold.isPending} onClick={() => hold.mutate([...selected])}>{hold.isPending ? 'Holding seats…' : 'Pay'}</button>
        <small>Maximum 6 seats per booking.</small>
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
    <div className="page-heading"><p className="kicker">YOUR BOX OFFICE</p><h1>My tickets</h1><p>Booking references and seats, ready when you need them.</p></div>
    {query.isLoading
      ? <Skeleton />
      : query.isError
        ? <ErrorState message="We couldn't load your tickets." retry={() => query.refetch()} />
        : query.data?.length === 0
          ? <div className="empty"><p>You don't have any tickets yet.</p><Link className="button" to="/">Browse events</Link></div>
          : <div className="tickets">{query.data?.map(booking => <article className="ticket" key={booking.id}>
            <div className="ticket-main"><span className={`status ${booking.status.toLowerCase()}`}>{bookingStatus(booking.status)}</span><p className="ticket-number">{booking.bookingNumber}</p><p>{date(booking.createdAt)}</p><div className="ticket-seats">{booking.items.map(seat => <span key={seat.seatId}>{seat.section} {seat.row}{seat.number}</span>)}</div></div>
            <div className="ticket-stub"><span>{booking.items.length} seat{booking.items.length === 1 ? '' : 's'}</span><strong>{money(booking.totalAmount, booking.currency)}</strong><small>FLASHSEAT</small></div>
          </article>)}</div>}
  </section>;
}

function Skeleton({ label = 'Loading' }: { label?: string }) {
  return <div className="skeleton" role="status"><span className="sr-only">{label}</span></div>;
}

function ErrorState({ message, retry }: { message: string; retry: () => unknown }) {
  return <div className="empty" role="alert"><p>{message}</p><button className="ghost" onClick={retry}>Try again</button></div>;
}
