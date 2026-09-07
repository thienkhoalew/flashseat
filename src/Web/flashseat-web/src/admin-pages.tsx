import { cloneElement, useEffect, useId, useMemo, useRef, useState, type ReactElement } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { z } from 'zod';
import { api, date, type EventDetail, money, type SaveEventInput } from './api';
import { emptySeatLayout, layoutFromSeats, serializeSeatLayout, SeatLayoutEditor, type SeatLayoutDraft } from './seat-layout-editor';

const seatSchema = z.object({
  section: z.string().min(1, 'Section is required').max(50),
  row: z.string().min(1, 'Row is required').max(10),
  number: z.number().int().positive('Seat number must be positive'),
  price: z.number().positive('Price must be positive'),
  currency: z.string().regex(/^[A-Z]{3}$/, 'Use a three-letter currency code'),
});
const eventSchema = z.object({
  name: z.string().min(3).max(150),
  slug: z.string().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/, 'Use lowercase words separated by hyphens'),
  description: z.string().max(5000),
  imageUrl: z.string().url().startsWith('https://', 'Image URL must use HTTPS'),
  venueName: z.string().min(1).max(200),
  address: z.string().min(1).max(500),
  startsAt: z.string().min(1),
  endsAt: z.string().min(1),
  salesStartAt: z.string().min(1),
  salesEndAt: z.string().min(1),
  stageShape: z.enum(['Proscenium', 'Thrust', 'Arena', 'InTheRound']),
  seats: z.array(seatSchema).min(1, 'Add at least one seat'),
}).superRefine((value, context) => {
  const start = new Date(value.startsAt);
  const salesStart = new Date(value.salesStartAt);
  const endsAt = new Date(value.endsAt);
  const salesEnd = new Date(value.salesEndAt);
  if (endsAt <= start) context.addIssue({ code: 'custom', path: ['endsAt'], message: 'Event end must be after event start' });
  if (salesEnd <= salesStart) context.addIssue({ code: 'custom', path: ['salesEndAt'], message: 'Sales end must be after sales start' });
  if (start < salesEnd) context.addIssue({ code: 'custom', path: ['startsAt'], message: 'Event start must be after sales end' });
  const labels = new Set<string>();
  value.seats.forEach((seat, index) => {
    const label = `${seat.section}|${seat.row}|${seat.number}`.toLowerCase();
    if (labels.has(label)) context.addIssue({ code: 'custom', path: ['seats', index, 'number'], message: 'Seat labels must be unique' });
    labels.add(label);
  });
});
type EventForm = z.infer<typeof eventSchema>;
const localDate = (value: string) => {
  const date = new Date(value);
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 16);
};
const defaults = (event?: EventDetail): EventForm => event ? {
  ...event,
  startsAt: localDate(event.startsAt),
  endsAt: localDate(event.endsAt),
  salesStartAt: localDate(event.salesStartAt),
  salesEndAt: localDate(event.salesEndAt),
  stageShape: event.stageShape ?? 'Proscenium',
  seats: event.seats.map(seat => ({ section: seat.section, row: seat.row, number: seat.number, price: seat.price, currency: seat.currency, layoutX: seat.layoutX, layoutY: seat.layoutY })),
} : { name: '', slug: '', description: '', imageUrl: '', venueName: '', address: '', startsAt: '', endsAt: '', salesStartAt: '', salesEndAt: '', stageShape: 'Proscenium', seats: [] };

export function AdminEventsPage() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const qc = useQueryClient();
  const query = useQuery({ queryKey: ['admin-events', search, page], queryFn: () => api.adminEvents(search, page) });
  type ActionType = 'publish' | 'cancel' | 'unpublish' | 'restore' | 'republish' | 'archive';
  const action = useMutation({
    mutationFn: ({ id, type }: { id: string; type: ActionType }) => {
      switch (type) {
        case 'publish': return api.publishEvent(id);
        case 'cancel': return api.cancelEvent(id);
        case 'unpublish': return api.unpublishEvent(id);
        case 'restore': return api.restoreDraftEvent(id);
        case 'republish': return api.republishEvent(id);
        case 'archive': return api.archiveEvent(id);
      }
    },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin-events'] }); qc.invalidateQueries({ queryKey: ['events'] }); },
  });
  const run = (id: string, type: ActionType, name: string, actionLabel?: string) => {
    const messages: Record<ActionType, string> = {
      publish: `Publish ${name}? Published events can no longer be edited.`,
      cancel: `Cancel ${name}? It will disappear from public listings.`,
      unpublish: `Return ${name} to draft? This is only allowed before ticket sales begin.`,
      restore: `Restore ${name} to draft?`,
      republish: `Republish ${name}? Existing inventory and bookings will be preserved.`,
      archive: `${actionLabel ?? 'Archive'} ${name}? It will be removed while ticket history is preserved.`,
    };
    if (window.confirm(messages[type])) action.mutate({ id, type });
  };
  const pages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / query.data.pageSize)) : 1;
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const [menuDirection, setMenuDirection] = useState<'down' | 'up'>('down');
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const closeMenu = (event: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) setOpenMenuId(null);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpenMenuId(null);
    };
    document.addEventListener('mousedown', closeMenu);
    document.addEventListener('keydown', closeOnEscape);
    return () => {
      document.removeEventListener('mousedown', closeMenu);
      document.removeEventListener('keydown', closeOnEscape);
    };
  }, []);

  return <section className="admin-page">
    <div className="admin-toolbar" aria-busy={action.isPending}>
      <div><p className="kicker">BOX OFFICE ADMIN</p><h1>Events</h1><p>{query.data?.totalCount ?? '—'} records</p></div>
      <Link className="button" to="/admin/events/new">Create event</Link>
    </div>
    <label className="search"><span>Search records</span><input type="search" placeholder="Event or venue" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
    {query.isLoading
      ? <div className="skeleton" role="status"><span className="sr-only">Loading events</span></div>
      : query.isError
        ? <div className="empty" role="alert"><p>We couldn't load event records.</p><button className="ghost" onClick={() => query.refetch()}>Try again</button></div>
        : query.data?.items.length === 0
          ? <p className="empty">No events match “{search}”.</p>
          : <div className="admin-list">
            <div className="admin-list-head" aria-hidden="true"><span>Status</span><span>Event</span><span>Venue / date</span><span>From</span><span>Actions</span></div>
            {query.data?.items.map(event => <article className="admin-row" key={event.id}>
              <div><span className="admin-cell-label">Status</span><span className={`status ${event.status.toLowerCase()}`}>{event.status}</span></div>
              <div><span className="admin-cell-label">Event</span><h2>{event.name}</h2></div>
              <div><span className="admin-cell-label">Venue / date</span><p>{event.venueName}</p><p className="mono">{date(event.startsAt)}</p></div>
              <div><span className="admin-cell-label">From</span><strong className="mono">{money(event.minPrice, event.currency)}</strong></div>
              <div className="admin-actions" ref={openMenuId === event.id ? menuRef : undefined}>
                <button
                  className="admin-menu-trigger"
                  type="button"
                  aria-label={`Actions for ${event.name}`}
                  aria-haspopup="menu"
                  aria-expanded={openMenuId === event.id}
                  onClick={clickEvent => {
                    const rowBottom = clickEvent.currentTarget.closest('.admin-row')?.getBoundingClientRect().bottom ?? 0;
                    setMenuDirection(window.innerHeight - rowBottom < 190 ? 'up' : 'down');
                    setOpenMenuId(current => current === event.id ? null : event.id);
                  }}
                >
                  <span aria-hidden="true">•••</span>
                </button>
                {openMenuId === event.id && <div className={`admin-action-menu ${menuDirection === 'up' ? 'is-up' : ''}`} role="menu">
                  {event.status === 'Draft' && <>
                    <Link role="menuitem" className="admin-action-item" to={`/admin/events/${event.id}/edit`} onClick={() => setOpenMenuId(null)}>Edit event</Link>
                    <button role="menuitem" className="admin-action-item is-primary" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'publish', event.name); }}>Publish</button>
                    <button role="menuitem" className="admin-action-item is-danger" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'cancel', event.name); }}>Cancel</button>
                    <button role="menuitem" className="admin-action-item is-danger" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'archive', event.name); }}>Archive</button>
                  </>}
                  {event.status === 'Published' && <>
                    <Link role="menuitem" className="admin-action-item" to={`/events/${event.id}`} onClick={() => setOpenMenuId(null)}>View public event</Link>
                    <button role="menuitem" className="admin-action-item" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'unpublish', event.name); }}>Return to draft</button>
                    <button role="menuitem" className="admin-action-item is-danger" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'cancel', event.name); }}>Cancel</button>
                  </>}
                  {event.status === 'Cancelled' && <>
                    <button role="menuitem" className="admin-action-item is-primary" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'republish', event.name); }}>Republish</button>
                    <button role="menuitem" className="admin-action-item" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'restore', event.name); }}>Restore draft</button>
                    <button role="menuitem" className="admin-action-item is-danger" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'archive', event.name, 'Delete'); }}>Delete</button>
                  </>}
                  {event.status === 'Ended' && <button role="menuitem" className="admin-action-item is-danger" disabled={action.isPending} onClick={() => { setOpenMenuId(null); run(event.id, 'archive', event.name, 'Delete'); }}>Delete</button>}
                </div>}
              </div>
            </article>)}
          </div>}
    {action.isError && <p className="error" role="alert">{action.error instanceof Error && 'problem' in action.error && typeof action.error.problem === 'object' && action.error.problem !== null && 'code' in action.error.problem && typeof action.error.problem.code === 'string' ? `${action.error.problem.code}: ` : ''}{action.error.message}</p>}
    {!query.isError && query.data && query.data.items.length > 0 && <nav className="pagination" aria-label="Admin event pages"><button className="ghost" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button><span className="mono" aria-live="polite">Page {page} / {pages}</span><button className="ghost" disabled={page >= pages} onClick={() => setPage(value => value + 1)}>Next</button></nav>}
  </section>;
}

export function AdminEventFormPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const event = useQuery({ queryKey: ['admin-event', id], queryFn: () => api.adminEvent(id!), enabled: !!id });
  if (id && event.isLoading) return <div className="skeleton" role="status"><span className="sr-only">Loading event</span></div>;
  if (id && event.isError) return <section className="empty" role="alert"><p>We couldn't load this event.</p><button className="ghost" onClick={() => event.refetch()}>Try again</button></section>;
  if (id && !event.data) return null;
  return <EventFormPage event={event.data} onSaved={() => { qc.invalidateQueries({ queryKey: ['admin-events'] }); if (id) qc.invalidateQueries({ queryKey: ['admin-event', id] }); nav('/admin/events'); }} />;
}

function EventFormPage({ event, onSaved }: { event?: EventDetail; onSaved: () => void }) {
  const initialLayout = useMemo(() => event ? layoutFromSeats(event.stageShape ?? 'Proscenium', event.seats, event.stageX, event.stageY) : emptySeatLayout(), [event]);
  const [layout, setLayout] = useState<SeatLayoutDraft>(initialLayout);
  const form = useForm<EventForm>({ resolver: zodResolver(eventSchema), defaultValues: defaults(event) });
  const save = useMutation({
    mutationFn: (value: EventForm) => {
      const seats = serializeSeatLayout(layout);
      const input: SaveEventInput = { ...value, stageShape: layout.stageShape, stageX: layout.stageX, stageY: layout.stageY, seats: seats.map(seat => ({ section: seat.section, row: seat.row, number: seat.number, price: seat.price, currency: seat.currency, layoutX: seat.layoutX, layoutY: seat.layoutY })), startsAt: new Date(value.startsAt).toISOString(), endsAt: new Date(value.endsAt).toISOString(), salesStartAt: new Date(value.salesStartAt).toISOString(), salesEndAt: new Date(value.salesEndAt).toISOString() };
      return event ? api.updateEvent(event.id, input) : api.createEvent(input);
    },
    onSuccess: onSaved,
  });
  const inventoryError = layout.seats.length === 0
    ? 'Add at least one seat before saving.'
    : layout.seats.some(seat => !seat.row)
      ? 'Assign every seat to a row before saving.'
      : undefined;
  const submit = (value: EventForm) => {
    const seats = serializeSeatLayout(layout);
    const validation = eventSchema.safeParse({ ...value, stageShape: layout.stageShape, seats });
    if (!validation.success) {
      form.setError('seats', { type: 'validate', message: inventoryError ?? 'Check the seat layout before saving.' });
      return;
    }
    save.mutate(value);
  };

  return <section className="admin-page">
    <p className="kicker">BOX OFFICE ADMIN</p><h1>{event ? 'Edit event' : 'Create event'}</h1>
    {event && event.status !== 'Draft'
      ? <p className="error" role="alert">Only draft events can be edited.</p>
      : <form className="event-form" aria-busy={save.isPending} onSubmit={form.handleSubmit(submit)}>
        <fieldset><legend>Event details</legend><div className="form-grid">
          <Field label="Name" error={form.formState.errors.name?.message}><input {...form.register('name')} /></Field>
          <Field label="Slug" error={form.formState.errors.slug?.message}><input {...form.register('slug')} /></Field>
          <Field label="Image URL" error={form.formState.errors.imageUrl?.message}><input type="url" {...form.register('imageUrl')} /></Field>
          <Field className="wide" label="Description" error={form.formState.errors.description?.message}><textarea rows={5} {...form.register('description')} /></Field>
        </div></fieldset>
        <fieldset><legend>Venue</legend><div className="form-grid">
          <Field label="Venue" error={form.formState.errors.venueName?.message}><input {...form.register('venueName')} /></Field>
          <Field label="Address" error={form.formState.errors.address?.message}><input {...form.register('address')} /></Field>
        </div></fieldset>
        <fieldset><legend>Schedule</legend><div className="form-grid">
          <Field label="Event start" error={form.formState.errors.startsAt?.message}><input type="datetime-local" {...form.register('startsAt')} /></Field>
          <Field label="Event end" error={form.formState.errors.endsAt?.message}><input type="datetime-local" {...form.register('endsAt')} /></Field>
          <Field label="Sales start" error={form.formState.errors.salesStartAt?.message}><input type="datetime-local" {...form.register('salesStartAt')} /></Field>
          <Field label="Sales end" error={form.formState.errors.salesEndAt?.message}><input type="datetime-local" {...form.register('salesEndAt')} /></Field>
        </div></fieldset>
        <fieldset className="seat-editor" aria-invalid={!!inventoryError} aria-describedby={inventoryError ? 'seat-inventory-error' : undefined}><legend>Seat layout</legend>
          <p>Choose a stage shape, create seat types, then add complete rows by selecting a section, seat type and quantity. Each row is numbered from 1 automatically.</p>
          <SeatLayoutEditor value={layout} onChange={setLayout} />
          {inventoryError && <p id="seat-inventory-error" className="error" role="alert">{inventoryError}</p>}
        </fieldset>
        {save.isError && <p className="error" role="alert">{save.error.message}</p>}
        <div className="admin-actions"><button className="button" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save draft'}</button><Link className="ghost" to="/admin/events">Cancel</Link></div>
      </form>}
  </section>;
}

function Field({ label, error, className, children }: { label: string; error?: string; className?: string; children: ReactElement<{ 'aria-invalid'?: boolean; 'aria-describedby'?: string }> }) {
  const id = useId();
  const errorId = `${id}-error`;
  return <label className={`field${className ? ` ${className}` : ''}`}><span>{label}</span>{cloneElement(children, { 'aria-invalid': !!error, 'aria-describedby': error ? errorId : undefined })}{error && <small id={errorId}>{error}</small>}</label>;
}
