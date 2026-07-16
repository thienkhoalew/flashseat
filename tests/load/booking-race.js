import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';

export const options = { vus: 100, iterations: 100 };
const baseUrl = __ENV.BASE_URL || 'http://localhost:5000';
const tokens = (__ENV.TOKENS || '').split(',').filter(Boolean);
const eventId = __ENV.EVENT_ID;
const seatId = __ENV.SEAT_ID;

export default function () {
  const token = tokens[exec.scenario.iterationInTest % tokens.length];
  const response = http.post(`${baseUrl}/api/seat-holds`, JSON.stringify({ eventId, seatIds: [seatId] }), {
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
  });
  check(response, { '201 or 409 only': (r) => r.status === 201 || r.status === 409 });
}

export function handleSummary(data) {
  const statuses = data.metrics.http_reqs?.values || {};
  return { stdout: `requests=${statuses.count || 0} p95=${data.metrics.http_req_duration.values['p(95)']}ms\n` };
}
