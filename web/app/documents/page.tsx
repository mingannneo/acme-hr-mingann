"use client";
import { useState } from "react";
import { api, StoredDocument } from "@/lib/api";

export default function Documents() {
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<StoredDocument | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!file) return;
    setBusy(true); setError(""); setResult(null);
    try {
      const form = new FormData();
      form.append("file", file);
      setResult(await api<StoredDocument>("/documents", { method: "POST", body: form }));
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  }

  return (
    <>
      <div className="card">
        <h2>Submit an MC or expense claim</h2>
        <p>Choose a PDF (max 5 MB, 5 pages). Samples are in <code>seed/documents</code>: some are text PDFs, some are scans.</p>
        <form onSubmit={submit}>
          <p><input type="file" accept="application/pdf,.pdf" onChange={e => setFile(e.target.files?.[0] ?? null)} /></p>
          <button disabled={busy || !file}>{busy ? "Reading…" : "Submit"}</button>
        </form>
        {error && <p className="error">{error}</p>}
      </div>
      {result && (
        <div className="card">
          <h3>{result.status === "Valid" ? "Accepted" : "Sent to HR for review"}</h3>
          <p>Kind: {result.kind}{result.problems && <> · Problems: {result.problems}</>}</p>
          <pre style={{ whiteSpace: "pre-wrap" }}>{result.extractedJson && JSON.stringify(JSON.parse(result.extractedJson), null, 2)}</pre>
        </div>
      )}
    </>
  );
}
