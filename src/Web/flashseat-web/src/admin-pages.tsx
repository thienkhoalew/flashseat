import { cloneElement, useId, useState, type ReactElement } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { z } from 'zod';
import { api, date, type EventDetail, money, type SaveEventInput } from './api';

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
  salesStartAt: z.string().min(1),
  salesEndAt: z.string().min(1),
  seats: z.array(seatSchema).min(1, 'Add at least one seat'),
}).superRefine((value, context) => {
  const start = new Date(value.startsAt);
  const salesStart = new Date(value.salesStartAt);
  const salesEnd = new Date(value.salesEndAt);
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
const emptySeat = { section: 'Standard', row: 'A', number: 1, price: 300000, currency: 'VND' };
const localDate = (value: string) => {
  const date = new Date(value);
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 16);
};
const defaults = (event?: EventDetail): EventForm => event ? {
  ...event,
  startsAt: localDate(event.startsAt),
  salesStartAt: localDate(event.salesStartAt),
  salesEndAt: localDate(event.salesEndAt),
  seats: event.seats.map(seat => ({ section: seat.section, row: seat.row, number: seat.number, price: seat.price, currency: seat.currency })),
} : { name: '', slug: '', description: '', imageUrl: '', venueName: '', address: '', startsAt: '', salesStartAt: '', salesEndAt: '', seats: [emptySeat] };

export function AdminEventsPage() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const qc = useQueryClient();
  const query = useQuery({ queryKey: ['admin-events', search, page], queryFn: () => api.adminEvents(search, page) });
  const action = useMutation({
    mutationFn: ({ id, type }: { id: string; type: 'publish' | 'cancel' }) => type === 'publish' ? api.publishEvent(id) : api.cancelEvent(id),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin-events'] }); qc.invalidateQueries({ queryKey: ['events'] }); },
  });
  const run = (id: string, type: 'publish' | 'cancel', name: string) => {
    const message = type === 'publish'
      ? `Publish ${name}? Published events can no longer be edited.`
      : `Cancel ${name}? It will disappear from public listings.`;
    if (window.confirm(message)) action.mutate({ id, type });
  };
  const pages = query.data ? Math.max(1, Math.ceil(query.data.totalCount / query.data.pageSize)) : 1;

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
              <div className="admin-actions">
                {event.status === 'Draft' && <><Link className="ghost" to={`/admin/events/${event.id}/edit`}>Edit</Link><button className="button small" disabled={action.isPending} onClick={() => run(event.id, 'publish', event.name)}>Publish</button></>}
                {event.status === 'Published' && <Link className="ghost" to={`/events/${event.id}`}>View public event</Link>}
                {['Draft', 'Published'].includes(event.status) && <button className="danger" disabled={action.isPending} onClick={() => run(event.id, 'cancel', event.name)}>Cancel</button>}
              </div>
            </article>)}
          </div>}
    {action.isError && <p className="error" role="alert">{action.error.message}</p>}
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
  const form = useForm<EventForm>({ resolver: zodResolver(eventSchema), defaultValues: defaults(event) });
  const seats = useFieldArray({ control: form.control, name: 'seats' });
  const save = useMutation({
    mutationFn: (value: EventForm) => {
      const input: SaveEventInput = { ...value, startsAt: new Date(value.startsAt).toISOString(), salesStartAt: new Date(value.salesStartAt).toISOString(), salesEndAt: new Date(value.salesEndAt).toISOString() };
      return event ? api.updateEvent(event.id, input) : api.createEvent(input);
    },
    onSuccess: onSaved,
  });
  const inventoryError = typeof form.formState.errors.seats?.message === 'string' ? form.formState.errors.seats.message : undefined;

  return <section className="admin-page">
    <p className="kicker">BOX OFFICE ADMIN</p><h1>{event ? 'Edit event' : 'Create event'}</h1>
    {event && event.status !== 'Draft'
      ? <p className="error" role="alert">Only draft events can be edited.</p>
      : <form className="event-form" aria-busy={save.isPending} onSubmit={form.handleSubmit(value => save.mutate(value))}>
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
          <Field label="Sales start" error={form.formState.errors.salesStartAt?.message}><input type="datetime-local" {...form.register('salesStartAt')} /></Field>
          <Field label="Sales end" error={form.formState.errors.salesEndAt?.message}><input type="datetime-local" {...form.register('salesEndAt')} /></Field>
        </div></fieldset>
        <fieldset className="seat-editor" aria-invalid={!!inventoryError} aria-describedby={inventoryError ? 'seat-inventory-error' : undefined}><legend>Seat inventory</legend>
          <div className="section-head"><div><h2>{seats.fields.length} seat{seats.fields.length === 1 ? '' : 's'}</h2><p>Each section, row and number combination must be unique.</p></div><button type="button" className="ghost" onClick={() => seats.append({ ...emptySeat, number: seats.fields.length + 1 })}>Add seat</button></div>
          <div className="seat-table-head" aria-hidden="true"><span>Section</span><span>Row</span><span>Number</span><span>Price</span><span>Currency</span><span /></div>
          {seats.fields.map((seat, index) => <div className="seat-row" key={seat.id}>
            <Field label="Section" error={form.formState.errors.seats?.[index]?.section?.message}><input {...form.register(`seats.${index}.section`)} /></Field>
            <Field label="Row" error={form.formState.errors.seats?.[index]?.row?.message}><input {...form.register(`seats.${index}.row`)} /></Field>
            <Field label="Number" error={form.formState.errors.seats?.[index]?.number?.message}><input type="number" {...form.register(`seats.${index}.number`, { valueAsNumber: true })} /></Field>
            <Field label="Price" error={form.formState.errors.seats?.[index]?.price?.message}><input type="number" step="0.01" {...form.register(`seats.${index}.price`, { valueAsNumber: true })} /></Field>
            <Field label="Currency" error={form.formState.errors.seats?.[index]?.currency?.message}><input maxLength={3} {...form.register(`seats.${index}.currency`)} /></Field>
            <button type="button" className="danger" disabled={seats.fields.length === 1} onClick={() => seats.remove(index)}>Remove</button>
          </div>)}
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
