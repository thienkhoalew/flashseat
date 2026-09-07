import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  closestCenter,
  useDroppable,
  useDraggable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core';

export type StageShape = 'Proscenium' | 'Thrust' | 'Arena' | 'InTheRound';
export type LayoutSeat = { id: string; section: string; row: string; number: number; price: number; currency: string; layoutX?: number; layoutY?: number };
export type Point = { x: number; y: number };
export type RowTransform = { x: number; y: number; rotation: number };
export type LayoutRow = { id: string; section: string; label: string; seatIds: string[]; transform?: RowTransform };
export type SeatType = { id: string; name: string; price: number; currency: string };
export type SeatLayoutDraft = { stageShape: StageShape; stageX?: number; stageY?: number; seatTypes: SeatType[]; rows: LayoutRow[]; seats: LayoutSeat[]; unassignedSeatIds: string[] };

type Props = { value: SeatLayoutDraft; onChange: (value: SeatLayoutDraft) => void };

const stages: { value: StageShape; label: string; description: string }[] = [
  { value: 'Proscenium', label: 'Proscenium', description: 'Audience faces a front stage.' },
  { value: 'Thrust', label: 'Thrust', description: 'Stage extends into the audience.' },
  { value: 'Arena', label: 'Arena', description: 'Audience surrounds one end.' },
  { value: 'InTheRound', label: 'In the round', description: 'Audience surrounds the stage.' },
];

const DEFAULT_ROW_SPACING = 3.1;
export const DEFAULT_STAGE_POSITION = { x: 50, y: 10 };
const STAGE_DIMENSIONS: Record<StageShape, { width: number; height: number }> = {
  Proscenium: { width: 330, height: 78 },
  Thrust: { width: 330, height: 78 },
  Arena: { width: 220, height: 110 },
  InTheRound: { width: 124, height: 124 },
};
const clamp = (value: number, min = 0, max = 100) => Math.min(max, Math.max(min, value));
const round = (value: number) => Math.round(clamp(value) * 100) / 100;

function validStageCoordinate(value: number | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

export function stagePosition(draft: SeatLayoutDraft): Point {
  return {
    x: validStageCoordinate(draft.stageX) ? clamp(draft.stageX) : DEFAULT_STAGE_POSITION.x,
    y: validStageCoordinate(draft.stageY) ? clamp(draft.stageY) : DEFAULT_STAGE_POSITION.y,
  };
}

function stageBounds(canvas: { width: number; height: number }, stage: { width?: number; height?: number }) {
  const width = canvas.width > 0 ? Math.min(100, (stage.width ?? 0) / canvas.width * 100) : 0;
  const height = canvas.height > 0 ? Math.min(100, (stage.height ?? 0) / canvas.height * 100) : 0;
  return { minX: width / 2, maxX: 100 - width / 2, minY: height / 2, maxY: 100 - height / 2 };
}

export function translateStage(draft: SeatLayoutDraft, delta: Point, canvas: { width: number; height: number }, stage: { width?: number; height?: number } = {}): SeatLayoutDraft {
  if (canvas.width <= 0 || canvas.height <= 0) return draft;
  const current = stagePosition(draft);
  const bounds = stageBounds(canvas, stage);
  const x = clamp(current.x + delta.x / canvas.width * 100, bounds.minX, bounds.maxX);
  const y = clamp(current.y + delta.y / canvas.height * 100, bounds.minY, bounds.maxY);
  return { ...draft, stageX: round(x), stageY: round(y) };
}

export function placeStage(draft: SeatLayoutDraft, point: Point, canvas: { left: number; top: number; width: number; height: number }, stage: { width?: number; height?: number } = {}): SeatLayoutDraft {
  if (canvas.width <= 0 || canvas.height <= 0) return draft;
  const bounds = stageBounds(canvas, stage);
  const x = clamp((point.x - canvas.left) / canvas.width * 100, bounds.minX, bounds.maxX);
  const y = clamp((point.y - canvas.top) / canvas.height * 100, bounds.minY, bounds.maxY);
  return { ...draft, stageX: round(x), stageY: round(y) };
}

function fitDelta(points: Point[], delta: Point): Point {
  if (points.length === 0) return delta;
  const minX = Math.min(...points.map(point => point.x));
  const maxX = Math.max(...points.map(point => point.x));
  const minY = Math.min(...points.map(point => point.y));
  const maxY = Math.max(...points.map(point => point.y));
  return {
    x: maxX - minX > 100 ? 50 - (minX + maxX) / 2 : clamp(delta.x, -minX, 100 - maxX),
    y: maxY - minY > 100 ? 50 - (minY + maxY) / 2 : clamp(delta.y, -minY, 100 - maxY),
  };
}

const finitePoint = (seat: LayoutSeat): seat is LayoutSeat & { layoutX: number; layoutY: number } =>
  typeof seat.layoutX === 'number' && Number.isFinite(seat.layoutX) && typeof seat.layoutY === 'number' && Number.isFinite(seat.layoutY);

export function centroid(points: Point[]): Point {
  if (points.length === 0) return { x: 50, y: 50 };
  return points.reduce((sum, point) => ({ x: sum.x + point.x / points.length, y: sum.y + point.y / points.length }), { x: 0, y: 0 });
}

export function rotatePoint(point: Point, center: Point, degrees: number): Point {
  const radians = degrees * Math.PI / 180;
  const cos = Math.cos(radians);
  const sin = Math.sin(radians);
  const x = point.x - center.x;
  const y = point.y - center.y;
  return { x: center.x + x * cos - y * sin, y: center.y + x * sin + y * cos };
}

export function rowAngle(seats: LayoutSeat[]): number {
  const positioned = seats.filter(finitePoint).sort((a, b) => a.number - b.number);
  if (positioned.length < 2) return 0;
  const first = positioned[0];
  const last = positioned[positioned.length - 1];
  const dx = last.layoutX - first.layoutX;
  const dy = last.layoutY - first.layoutY;
  return Math.abs(dx) + Math.abs(dy) < 0.001 ? 0 : Math.atan2(dy, dx) * 180 / Math.PI;
}

function pointFor(seat: LayoutSeat): Point | undefined {
  return finitePoint(seat) ? { x: seat.layoutX, y: seat.layoutY } : undefined;
}

function positionedSeats(row: LayoutRow, seats: LayoutSeat[]) {
  return row.seatIds.map(id => seats.find(seat => seat.id === id)).filter((seat): seat is LayoutSeat => !!seat);
}

function defaultRowCenter(index: number, count: number): Point {
  return { x: 50, y: 22 + (index * 60) / Math.max(1, count - 1) };
}

function initializeRowCoordinates(row: LayoutRow, seats: LayoutSeat[], rowIndex: number, rowCount: number): LayoutSeat[] {
  const rowSeats = positionedSeats(row, seats);
  const points = rowSeats.map(pointFor).filter((point): point is Point => !!point);
  const center = points.length ? centroid(points) : defaultRowCenter(rowIndex, rowCount);
  const angle = points.length > 1 ? rowAngle(rowSeats) : 0;
  const spacing = DEFAULT_ROW_SPACING;
  const middle = (rowSeats.length - 1) / 2;
  const next = seats.map(seat => ({ ...seat }));
  rowSeats.forEach((seat, index) => {
    if (finitePoint(seat)) return;
    const local = { x: (index - middle) * spacing, y: 0 };
    const point = rotatePoint({ x: center.x + local.x, y: center.y + local.y }, center, angle);
    const target = next.find(item => item.id === seat.id);
    if (target) {
      target.layoutX = round(point.x);
      target.layoutY = round(point.y);
    }
  });
  return next;
}

function normalizeRows(rows: LayoutRow[], seats: LayoutSeat[]): LayoutRow[] {
  return rows.map(row => ({
    ...row,
    seatIds: [...row.seatIds],
    transform: row.transform ?? { x: centroid(positionedSeats(row, seats).map(pointFor).filter((point): point is Point => !!point)).x, y: centroid(positionedSeats(row, seats).map(pointFor).filter((point): point is Point => !!point)).y, rotation: rowAngle(positionedSeats(row, seats)) },
  }));
}

export function renumberLayout(draft: SeatLayoutDraft): SeatLayoutDraft {
  let seats = draft.seats.map(seat => ({ ...seat }));
  const rows = normalizeRows(draft.rows, seats);
  rows.forEach(row => {
    row.seatIds.forEach((id, index) => {
      const seat = seats.find(item => item.id === id);
      if (seat) {
        seat.number = index + 1;
        seat.section = row.section;
        seat.row = row.label;
      }
    });
  });
  seats = rows.reduce((result, row, index) => initializeRowCoordinates(row, result, index, rows.length), seats);
  const assigned = new Set(rows.flatMap(row => row.seatIds));
  draft.unassignedSeatIds.forEach(id => {
    const seat = seats.find(item => item.id === id);
    if (seat) {
      seat.row = '';
      seat.layoutX = undefined;
      seat.layoutY = undefined;
    }
  });
  return { ...draft, rows, seats: seats.map(seat => assigned.has(seat.id) ? seat : { ...seat, layoutX: undefined, layoutY: undefined }) };
}

export function translateRow(draft: SeatLayoutDraft, rowId: string, delta: Point): SeatLayoutDraft {
  const row = draft.rows.find(item => item.id === rowId);
  if (!row) return draft;
  const ids = new Set(row.seatIds);
  const points = draft.seats.filter(seat => ids.has(seat.id)).map(pointFor).filter((point): point is Point => !!point);
  const fittedDelta = fitDelta(points, delta);
  const seats = draft.seats.map(seat => ids.has(seat.id) && finitePoint(seat)
    ? { ...seat, layoutX: round(seat.layoutX + fittedDelta.x), layoutY: round(seat.layoutY + fittedDelta.y) }
    : { ...seat });
  const rows = draft.rows.map(item => {
    if (item.id !== rowId) return item;
    const current = item.transform ?? { x: 50, y: 50, rotation: 0 };
    return { ...item, transform: { ...current, x: clamp(current.x + fittedDelta.x), y: clamp(current.y + fittedDelta.y) } };
  });
  return renumberLayout({ ...draft, seats, rows });
}

export function rotateRow(draft: SeatLayoutDraft, rowId: string, degrees: number): SeatLayoutDraft {
  const row = draft.rows.find(item => item.id === rowId);
  if (!row) return draft;
  const rowSeats = positionedSeats(row, draft.seats);
  const center = centroid(rowSeats.map(pointFor).filter((point): point is Point => !!point));
  const ids = new Set(row.seatIds);
  const rotated = rowSeats.filter(finitePoint).map(seat => ({
    id: seat.id,
    point: rotatePoint({ x: seat.layoutX, y: seat.layoutY }, center, degrees),
  }));
  const fittedDelta = fitDelta(rotated.map(item => item.point), { x: 0, y: 0 });
  const rotatedById = new Map(rotated.map(item => [item.id, item.point]));
  const seats = draft.seats.map(seat => {
    const point = ids.has(seat.id) ? rotatedById.get(seat.id) : undefined;
    return point
      ? { ...seat, layoutX: round(point.x + fittedDelta.x), layoutY: round(point.y + fittedDelta.y) }
      : { ...seat };
  });
  const current = row.transform ?? { x: center.x, y: center.y, rotation: rowAngle(rowSeats) };
  const rows = draft.rows.map(item => item.id === rowId
    ? { ...item, transform: { ...current, rotation: current.rotation + degrees } }
    : item);
  return renumberLayout({ ...draft, seats, rows });
}

const stageClass = (shape: StageShape) => `stage-shape stage-shape-${shape.toLowerCase()}`;
const seatById = (draft: SeatLayoutDraft, id: string) => draft.seats.find(seat => seat.id === id);
const makeId = (prefix: string) => `${prefix}-${crypto.randomUUID()}`;

function StagePreview({ shape }: { shape: StageShape }) {
  return <div className={stageClass(shape)} aria-label={`${shape} stage preview`} />;
}

function StagePaletteItem({ stage, selected, onSelect }: { stage: (typeof stages)[number]; selected: boolean; onSelect: () => void }) {
  const draggable = useDraggable({ id: `stage-palette:${stage.value}`, data: { type: 'stage-palette', stage: stage.value } });
  return <button ref={draggable.setNodeRef} className={`stage-preset ${selected ? 'is-selected' : ''}`} style={{ opacity: draggable.isDragging ? .45 : 1 }} type="button" onClick={onSelect} {...draggable.listeners} {...draggable.attributes} aria-pressed={selected}>
    <StagePreview shape={stage.value} /><strong>{stage.label}</strong><small>{stage.description}</small>
  </button>;
}

const SECTION_COLORS = ['#e64a19', '#1565c0', '#2e7d32', '#6a1b9a', '#c62828', '#00838f', '#ef6c00', '#4527a0', '#ad1457', '#00695c'];
function sectionColor(sections: string[], section: string): string {
  const idx = sections.indexOf(section);
  return idx < 0 ? SECTION_COLORS[0] : SECTION_COLORS[idx % SECTION_COLORS.length];
}

function LayoutSeatMarker({ seat, color, style }: { seat: LayoutSeat; color?: string; style?: React.CSSProperties }) {
  return <span className="layout-seat" style={{ ...style, background: color, color: color ? '#fff' : undefined, borderColor: color ?? undefined }} aria-label={`${seat.section} seat ${seat.row}${seat.number}`}>
    {seat.number}
  </span>;
}

function StageDropZone({ shape, position }: { shape: StageShape; position: Point }) {
  const draggable = useDraggable({ id: 'stage-instance', data: { type: 'stage-instance', stage: shape } });
  return <div ref={draggable.setNodeRef} className={`layout-stage ${draggable.isDragging ? 'is-dragging' : ''}`} style={{ left: `${position.x}%`, top: `${position.y}%`, opacity: draggable.isDragging ? .3 : 1 }} {...draggable.listeners} {...draggable.attributes}><StagePreview shape={shape} /></div>;
}

type RowGeometry = { center: Point; angle: number; minX: number; maxX: number; minY: number; maxY: number; local: Map<string, Point> };
function geometryForRow(row: LayoutRow, seats: LayoutSeat[]): RowGeometry {
  const rowSeats = positionedSeats(row, seats);
  const points = rowSeats.map(pointFor).filter((point): point is Point => !!point);
  const center = centroid(points);
  const angle = rowAngle(rowSeats);
  const local = new Map(rowSeats.map(seat => {
    const rotated = rotatePoint({ x: seat.layoutX!, y: seat.layoutY! }, center, -angle);
    return [seat.id, { x: rotated.x - center.x, y: rotated.y - center.y }];
  }));
  const locals = [...local.values()];
  return { center, angle, minX: Math.min(...locals.map(point => point.x), 0), maxX: Math.max(...locals.map(point => point.x), 0), minY: Math.min(...locals.map(point => point.y), 0), maxY: Math.max(...locals.map(point => point.y), 0), local };
}

function RowGroup({ row, draft, selected, onSelect, sections }: { row: LayoutRow; draft: SeatLayoutDraft; selected: boolean; onSelect: () => void; sections: string[] }) {
  const draggable = useDraggable({ id: `row:${row.id}`, data: { type: 'row', rowId: row.id } });
  const geometry = geometryForRow(row, draft.seats);
  const localWidth = geometry.maxX - geometry.minX;
  const localHeight = geometry.maxY - geometry.minY;
  const width = Math.max(0, localWidth);
  const height = Math.max(0, localHeight);
  const color = sectionColor(sections, row.section);
  return <div className={`layout-row-group ${selected ? 'is-selected' : ''}`} style={{ left: `${geometry.center.x}%`, top: `${geometry.center.y}%`, width: `calc(${width}% + var(--layout-row-extra))`, height: `calc(${height}% + var(--layout-row-extra))`, transform: `translate(-50%, -50%) rotate(${geometry.angle}deg)` }} onClick={e => { e.stopPropagation(); onSelect(); }}>
    <span ref={draggable.setNodeRef} className="row-tag" style={{ background: color }} {...draggable.listeners} {...draggable.attributes} aria-label={`Move ${row.section} row ${row.label}`}>{row.section} {row.label}</span>
    <div className="layout-row-local-seats">
      {row.seatIds.map(id => {
        const seat = seatById(draft, id);
        const point = geometry.local.get(id);
        if (!seat || !point) return null;
        return <LayoutSeatMarker key={id} seat={seat} color={color} style={{ left: `${localWidth === 0 ? 50 : ((point.x - geometry.minX) / localWidth) * 100}%`, top: `${localHeight === 0 ? 50 : ((point.y - geometry.minY) / localHeight) * 100}%` }} />;
      })}
    </div>
  </div>;
}

export function emptySeatLayout(): SeatLayoutDraft {
  return { stageShape: 'Proscenium', stageX: DEFAULT_STAGE_POSITION.x, stageY: DEFAULT_STAGE_POSITION.y, seatTypes: [], rows: [], seats: [], unassignedSeatIds: [] };
}

export function layoutFromSeats(stageShape: StageShape, sourceSeats: LayoutSeat[], stageX?: number, stageY?: number): SeatLayoutDraft {
  const seats = sourceSeats.map(seat => ({ ...seat }));
  const types = [...new Map(seats.map(seat => [`${seat.section}|${seat.price}|${seat.currency}`, { id: `type-${seat.section}-${seat.price}`, name: seat.section, price: seat.price, currency: seat.currency }])).values()];
  const rows = [...new Map(seats.filter(seat => seat.row).map(seat => [`${seat.section}|${seat.row}`, { id: `row-${seat.section}-${seat.row}`, section: seat.section, label: seat.row, seatIds: [] as string[] }])).values()];
  rows.forEach(row => row.seatIds = seats.filter(seat => seat.section === row.section && seat.row === row.label).sort((a, b) => a.number - b.number).map(seat => seat.id));
  const initialized = rows.reduce((result, row, index) => initializeRowCoordinates(row, result, index, rows.length), seats);
  return renumberLayout({ stageShape, stageX: validStageCoordinate(stageX) && validStageCoordinate(stageY) ? clamp(stageX) : DEFAULT_STAGE_POSITION.x, stageY: validStageCoordinate(stageX) && validStageCoordinate(stageY) ? clamp(stageY) : DEFAULT_STAGE_POSITION.y, seatTypes: types, rows, seats: initialized, unassignedSeatIds: seats.filter(seat => !seat.row).map(seat => seat.id) });
}

export function serializeSeatLayout(value: SeatLayoutDraft): LayoutSeat[] {
  return value.seats.filter(seat => seat.row).map(seat => ({ ...seat }));
}

export function appendRowWithSeats(draft: SeatLayoutDraft, section: string, label: string, type: SeatType, quantity: number): SeatLayoutDraft {
  const safeQuantity = Math.max(1, Math.floor(quantity));
  const rowId = makeId('row');
  const rowSeats: LayoutSeat[] = Array.from({ length: safeQuantity }, (_, index) => ({
    id: makeId('seat'), section, row: label, number: index + 1, price: type.price, currency: type.currency,
  }));
  const row: LayoutRow = { id: rowId, section, label, seatIds: rowSeats.map(seat => seat.id) };
  return renumberLayout({ ...draft, rows: [...draft.rows, row], seats: [...draft.seats, ...rowSeats] });
}

export function SeatLayoutEditor({ value, onChange }: Props) {
  const [activeId, setActiveId] = useState<string>();
  const [activeStageShape, setActiveStageShape] = useState<StageShape>();
  const [newType, setNewType] = useState({ name: '', price: '', currency: 'VND' });
  const [newRow, setNewRow] = useState({ section: '', label: '', seatTypeId: '', quantity: '10' });
  const [selectedRowId, setSelectedRowId] = useState<string>();
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const canvasDrop = useDroppable({ id: 'stage-canvas', data: { type: 'stage-canvas' } });
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));
  const sections = useMemo(() => [...new Set(value.rows.map(row => row.section))], [value.rows]);
  const update = (next: SeatLayoutDraft) => onChange(renumberLayout(next));
  const handleRotate = useCallback((degrees: number) => {
    if (!selectedRowId) return;
    onChange(renumberLayout(rotateRow(value, selectedRowId, degrees)));
  }, [selectedRowId, value, onChange]);
  const handleMove = useCallback((delta: Point) => {
    if (!selectedRowId) return;
    onChange(renumberLayout(translateRow(value, selectedRowId, delta)));
  }, [selectedRowId, value, onChange]);
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (!selectedRowId) return;
      const target = e.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable) return;
      const step = e.shiftKey ? 5 : 1;
      const movement: Record<string, Point> = {
        ArrowUp: { x: 0, y: -step },
        ArrowDown: { x: 0, y: step },
        ArrowLeft: { x: -step, y: 0 },
        ArrowRight: { x: step, y: 0 },
      };
      if (movement[e.key]) { e.preventDefault(); handleMove(movement[e.key]); return; }
      if (e.key === 'r' || e.key === 'R') { e.preventDefault(); handleRotate(e.shiftKey ? -15 : 15); }
      if (e.key === '0') { e.preventDefault(); const row = value.rows.find(r => r.id === selectedRowId); if (row) handleRotate(-rowAngle(positionedSeats(row, value.seats))); }
      if (e.key === 'Escape') setSelectedRowId(undefined);
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [selectedRowId, handleRotate, value]);
  const addType = () => {
    if (!newType.name.trim()) return;
    const type = { id: makeId('type'), name: newType.name.trim(), price: Number(newType.price) || 0, currency: newType.currency.toUpperCase() };
    update({ ...value, seatTypes: [...value.seatTypes, type] });
    setNewType({ name: '', price: '', currency: 'VND' });
    setNewRow(current => ({ ...current, seatTypeId: type.id }));
  };
  const removeType = (typeId: string) => {
    update({
      ...value,
      seatTypes: value.seatTypes.filter(t => t.id !== typeId),
    });
    if (newRow.seatTypeId === typeId) {
      setNewRow(current => ({ ...current, seatTypeId: '' }));
    }
  };
  const addRow = () => {
    const type = value.seatTypes.find(item => item.id === newRow.seatTypeId) ?? value.seatTypes[0];
    const quantity = Number(newRow.quantity);
    if (!type || !newRow.section.trim() || !newRow.label.trim() || !Number.isInteger(quantity) || quantity < 1 || quantity > 500) return;
    const next = appendRowWithSeats(value, newRow.section.trim(), newRow.label.trim(), type, quantity);
    update(next);
    setNewRow(current => ({ ...current, label: '' }));
  };
  const handleDragStart = ({ active }: DragStartEvent) => {
    setActiveId(String(active.id));
    setActiveStageShape(active.data.current?.stage as StageShape | undefined);
  };
  const handleDragEnd = ({ active, over, delta }: DragEndEvent) => {
    setActiveId(undefined);
    setActiveStageShape(undefined);
    const type = active.data.current?.type;
    const activeKey = String(active.id);
    const bounds = canvasRef.current?.getBoundingClientRect();
    if (type === 'stage-palette') {
      const translated = active.rect.current.translated;
      const dropPoint = translated ? { x: translated.left + translated.width / 2, y: translated.top + translated.height / 2 } : null;
      const insideCanvas = bounds && dropPoint && dropPoint.x >= bounds.left && dropPoint.x <= bounds.right && dropPoint.y >= bounds.top && dropPoint.y <= bounds.bottom;
      if (bounds && (String(over?.id) === 'stage-canvas' || insideCanvas)) {
        const shape = active.data.current?.stage as StageShape;
        const dimensions = STAGE_DIMENSIONS[shape];
        if (!dropPoint) return;
        update(placeStage({ ...value, stageShape: shape }, dropPoint, bounds, dimensions));
      }
      return;
    }
    if (type === 'stage-instance') {
      if (bounds && (String(over?.id) === 'stage-canvas' || !over)) {
        const dimensions = STAGE_DIMENSIONS[value.stageShape];
        update(translateStage(value, delta, bounds, dimensions));
      }
      return;
    }
    if (type === 'row' || activeKey.startsWith('row:')) {
      const rowId = String(active.data.current?.rowId ?? activeKey.slice(4));
      const bounds = canvasRef.current?.getBoundingClientRect();
      if (bounds && bounds.width > 0 && bounds.height > 0) update(translateRow(value, rowId, { x: delta.x / bounds.width * 100, y: delta.y / bounds.height * 100 }));
    }
  };
  return <DndContext sensors={sensors} collisionDetection={closestCenter} onDragStart={handleDragStart} onDragEnd={handleDragEnd} onDragCancel={() => { setActiveId(undefined); setActiveStageShape(undefined); }}>
    <div className="layout-editor">
      <aside className="layout-palette">
        <h3>Stage shapes</h3>{stages.map(stage => <StagePaletteItem key={stage.value} stage={stage} selected={value.stageShape === stage.value} onSelect={() => update({ ...value, stageShape: stage.value })} />)}
        <h3>Seat types</h3><div className="layout-inline-form"><input aria-label="Seat type name" value={newType.name} onChange={event => setNewType(current => ({ ...current, name: event.target.value }))} placeholder="VIP" /><input aria-label="Seat type price" type="number" value={newType.price} onChange={event => setNewType(current => ({ ...current, price: event.target.value }))} /><button type="button" className="ghost" onClick={addType}>Add type</button></div>
        {value.seatTypes.length === 0 ? <p className="layout-help">No seat types yet. Create a seat type above to start adding rows.</p> : null}
        {value.seatTypes.map(type => <div className="layout-palette-item" key={type.id}><span><strong>{type.name}</strong><small>{type.price} {type.currency}</small></span><button type="button" className="ghost danger-text" aria-label={`Remove seat type ${type.name}`} onClick={() => removeType(type.id)}>✕</button></div>)}
        <h3>Rows</h3><div className="layout-row-form"><input aria-label="Row section" value={newRow.section} onChange={event => setNewRow(current => ({ ...current, section: event.target.value }))} placeholder={sections[0] ?? 'Main'} /><input aria-label="Row label" value={newRow.label} onChange={event => setNewRow(current => ({ ...current, label: event.target.value }))} placeholder="A" /><select aria-label="Row seat type" value={newRow.seatTypeId || value.seatTypes[0]?.id || ''} onChange={event => setNewRow(current => ({ ...current, seatTypeId: event.target.value }))} disabled={value.seatTypes.length === 0}><option value="" disabled>Select seat type</option>{value.seatTypes.map(type => <option key={type.id} value={type.id}>{type.name}</option>)}</select><input aria-label="Row seat quantity" type="number" min="1" max="500" value={newRow.quantity} onChange={event => setNewRow(current => ({ ...current, quantity: event.target.value }))} /><button type="button" className="ghost" onClick={addRow} disabled={value.seatTypes.length === 0}>Add row</button></div>
        <p className="layout-help">Choose a section, row, seat type and quantity, then add the complete row. Each row starts at seat 1. Click a row to select it. <kbd>↑</kbd><kbd>↓</kbd><kbd>←</kbd><kbd>→</kbd> move 1%; hold <kbd>Shift</kbd> for 5%. <kbd>R</kbd> rotate, <kbd>Shift+R</kbd> reverse, <kbd>0</kbd> reset, <kbd>Esc</kbd> deselect.</p>
        {sections.length > 0 && <div className="layout-legend">{sections.map(s => <span key={s} className="layout-legend-item"><span className="layout-legend-swatch" style={{ background: sectionColor(sections, s) }} />{s}</span>)}</div>}
      </aside>
      <div className="layout-workspace"><div ref={node => { canvasRef.current = node; canvasDrop.setNodeRef(node); }} className={`layout-canvas ${canvasDrop.isOver ? 'is-stage-over' : ''}`} onClick={() => setSelectedRowId(undefined)}><StageDropZone shape={value.stageShape} position={stagePosition(value)} />{value.rows.map(row => <RowGroup key={row.id} row={row} draft={value} sections={sections} selected={selectedRowId === row.id} onSelect={() => setSelectedRowId(row.id)} />)}</div></div>
    </div><DragOverlay>{activeId?.startsWith('row:') ? <div className="layout-row-overlay">Moving row</div> : activeStageShape ? <div className="layout-stage layout-stage-overlay"><StagePreview shape={activeStageShape} /></div> : null}</DragOverlay>
  </DndContext>;
}
