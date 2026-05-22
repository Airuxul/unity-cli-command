/** Coerce CLI flag strings to types Unity expects in JSON parameters. */
export function coerceParameters(flags) {
  const out = { ...flags };
  if (out.compile === 'true' || out.compile === '1') out.compile = true;
  if (out.compile === 'false' || out.compile === '0') out.compile = false;
  if (out.clear === 'true' || out.clear === '1') out.clear = true;
  if (out.force === 'true' || out.force === '1') out.force = true;
  return out;
}
