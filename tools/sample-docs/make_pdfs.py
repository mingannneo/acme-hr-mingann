#!/usr/bin/env python3
"""Builds the 20 synthetic Lab 6 sample documents as PDF files in seed/documents/.

Run from the repo root: python3 tools/sample-docs/make_pdfs.py
Needs: pip install reportlab pillow, and pdftoppm (poppler) for the scanned ones.
All people, clinics, vendors and numbers are synthetic. Some documents are text PDFs (selectable
text); the ones in SCANS are image-only PDFs that look like a phone photo or a scan.
Planted on purpose: 08, 10, 17 are wrong on paper; 19, 20 are not MCs or claims.
Numeric dates are written day first (dd/MM/yyyy), as on Malaysian documents.
"""
import io, os, random, subprocess, tempfile
from reportlab.lib.pagesizes import A4, A5
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.pdfgen import canvas
from PIL import Image, ImageFilter, ImageEnhance, ImageOps

OUT = os.path.join("seed", "documents")
SCANS = {"03-mc", "06-mc", "09-mc", "13-claim", "16-claim", "20-other"}
INK = colors.HexColor("#1d2a4a"); GREY = colors.HexColor("#666666"); STAMP = colors.HexColor("#2b4fa8")

def foot(c, w):
    c.setFont("Helvetica-Oblique", 7); c.setFillColor(GREY)
    c.drawCentredString(w / 2, 10 * mm, "Synthetic document for training. Not a real person, clinic or business.")
    c.setFillColor(colors.black)

def stamp(c, x, y, top, bottom, r=17 * mm, angle=-12):
    c.saveState(); c.translate(x, y); c.rotate(angle); c.setStrokeColor(STAMP); c.setFillColor(STAMP)
    c.setLineWidth(1.6); c.circle(0, 0, r); c.setLineWidth(0.8); c.circle(0, 0, r - 3.2 * mm)
    c.setFont("Helvetica-Bold", 6.6)
    for i, ch in enumerate(top[:28]):  # text around the top of the ring
        a = 150 - i * (120 / max(len(top) - 1, 1)); c.saveState(); c.rotate(a); c.drawCentredString(0, r - 2.4 * mm, ch); c.restoreState()
    c.setFont("Helvetica-Bold", 7.5); c.drawCentredString(0, -1.2 * mm, bottom)
    c.restoreState()

def mc_formal(c, d):
    w, h = A4
    c.setFillColor(INK); c.rect(0, h - 34 * mm, w, 34 * mm, fill=1, stroke=0); c.setFillColor(colors.white)
    c.setFont("Helvetica-Bold", 18); c.drawString(20 * mm, h - 17 * mm, d["clinic"])
    c.setFont("Helvetica", 9); c.drawString(20 * mm, h - 24 * mm, d["addr"] + "   Tel: 03-0000 0000 (synthetic)")
    c.setFillColor(colors.black); c.setFont("Helvetica-Bold", 13); c.drawCentredString(w / 2, h - 52 * mm, "MEDICAL CERTIFICATE / SIJIL CUTI SAKIT")
    c.setFont("Helvetica", 9); c.drawRightString(w - 20 * mm, h - 62 * mm, "No. MC-" + d["no"])
    t = c.beginText(20 * mm, h - 78 * mm); t.setFont("Helvetica", 11.5); t.setLeading(17)
    for line in d["body"]: t.textLine(line)
    c.drawText(t)
    c.line(20 * mm, h - 140 * mm, 85 * mm, h - 140 * mm); c.setFont("Helvetica", 9.5); c.drawString(20 * mm, h - 145 * mm, d["doctor"])
    stamp(c, 140 * mm, h - 136 * mm, d["clinic"].upper(), "MMC 00000"); foot(c, w)

def mc_form(c, d):
    w, h = A5
    c.setFont("Helvetica-Bold", 15); c.drawCentredString(w / 2, h - 18 * mm, "Sijil Cuti Sakit")
    c.setFont("Helvetica", 8.5); c.drawCentredString(w / 2, h - 23 * mm, "Medical Certificate")
    y = h - 36 * mm
    for label, value in d["rows"]:
        c.setFillColor(colors.HexColor("#eef1f6")); c.rect(14 * mm, y - 2.5 * mm, 42 * mm, 8 * mm, fill=1, stroke=0); c.setFillColor(colors.black)
        c.setFont("Helvetica-Bold", 9); c.drawString(16 * mm, y, label)
        c.setFont("Helvetica", 10.5); c.drawString(60 * mm, y, value)
        c.setStrokeColor(colors.HexColor("#c8cdd8")); c.line(14 * mm, y - 2.6 * mm, w - 14 * mm, y - 2.6 * mm); c.setStrokeColor(colors.black)
        y -= 11 * mm
    c.setFont("Helvetica", 8.5); c.drawString(14 * mm, y - 10 * mm, "Tandatangan doktor: ____________________")
    stamp(c, w - 34 * mm, y - 8 * mm, d["rows"][0][1].upper(), "CAP KLINIK", r=14 * mm, angle=8); foot(c, w)

def mc_short(c, d):
    w, h = A5
    c.setFont("Helvetica-Bold", 26); c.drawString(14 * mm, h - 24 * mm, "MC")
    c.setFont("Helvetica-Bold", 11); c.drawString(14 * mm, h - 33 * mm, d["clinic"])
    c.setFont("Helvetica", 7.5); c.drawString(14 * mm, h - 38 * mm, "Klinik panel / Panel clinic (synthetic)")
    from reportlab.lib.utils import simpleSplit
    lines = simpleSplit(d["text"], "Helvetica", 11, w - 28 * mm)
    t = c.beginText(14 * mm, h - 54 * mm); t.setFont("Helvetica", 11); t.setLeading(16)
    for line in lines: t.textLine(line)
    t.textLine(""); t.textLine(d["remarks"]); c.drawText(t)
    c.line(14 * mm, 40 * mm, 70 * mm, 40 * mm); c.setFont("Helvetica", 8.5); c.drawString(14 * mm, 35 * mm, "Doctor (synthetic)")
    stamp(c, w - 32 * mm, 45 * mm, d["clinic"].upper(), "MC", r=14 * mm, angle=-20); foot(c, w)

def claim_form(c, d):
    w, h = A4
    c.setFont("Helvetica-Bold", 16); c.drawString(20 * mm, h - 25 * mm, "EXPENSE CLAIM"); c.setFont("Helvetica", 12); c.drawRightString(w - 20 * mm, h - 25 * mm, d["no"])
    c.setStrokeColor(INK); c.setLineWidth(1.2); c.line(20 * mm, h - 29 * mm, w - 20 * mm, h - 29 * mm); c.setLineWidth(1)
    y = h - 42 * mm
    for label, value in [("Vendor", d["vendor"]), ("Date", d["date"]), ("Currency", d["cur"]), ("Employee", "Acme HR Sdn Bhd staff (synthetic)")]:
        c.setFont("Helvetica-Bold", 10); c.drawString(20 * mm, y, label); c.setFont("Helvetica", 10.5); c.drawString(55 * mm, y, value); y -= 7 * mm
    y -= 6 * mm; c.setFillColor(colors.HexColor("#eef1f6")); c.rect(20 * mm, y - 2 * mm, w - 40 * mm, 8 * mm, fill=1, stroke=0); c.setFillColor(colors.black)
    c.setFont("Helvetica-Bold", 10); c.drawString(23 * mm, y, "Description"); c.drawRightString(w - 23 * mm, y, "Amount")
    y -= 9 * mm; c.setFont("Helvetica", 10.5)
    for desc, amt in d["items"]: c.drawString(23 * mm, y, desc); c.drawRightString(w - 23 * mm, y, amt); y -= 7 * mm
    c.line(110 * mm, y + 3 * mm, w - 20 * mm, y + 3 * mm); y -= 3 * mm
    for label, amt, bold in [("Subtotal", d["sub"], False), ("SST", d["tax"], False), ("TOTAL", d["total"], True)]:
        c.setFont("Helvetica-Bold" if bold else "Helvetica", 11 if bold else 10.5); c.drawString(110 * mm, y, label); c.drawRightString(w - 23 * mm, y, amt); y -= 7 * mm
    c.setFont("Helvetica", 9); c.drawString(20 * mm, y - 18 * mm, "Approved by: ____________________     Date: ____________")
    foot(c, w)

def receipt(c, d):
    w, h = 80 * mm, 150 * mm
    c.setPageSize((w, h)); c.setFont("Courier-Bold", 10.5); c.drawCentredString(w / 2, h - 12 * mm, d["vendor"])
    c.setFont("Courier", 7.5); c.drawCentredString(w / 2, h - 16 * mm, d["addr"])
    c.setFont("Courier-Bold", 9); c.drawCentredString(w / 2, h - 23 * mm, "OFFICIAL RECEIPT")
    y = h - 31 * mm; c.setFont("Courier", 8.5)
    for line in [f"Date : {d['date']}", f"Ref  : {d['no']}", "-" * 34]: c.drawString(6 * mm, y, line); y -= 4.6 * mm
    for desc, amt in d["items"]: c.drawString(6 * mm, y, desc[:22]); c.drawRightString(w - 6 * mm, y, amt); y -= 4.6 * mm
    c.drawString(6 * mm, y, "-" * 34); y -= 4.6 * mm
    for label, amt in [("Subtotal", d["sub"]), (d["taxlabel"], d["tax"]), ("TOTAL", d["total"])]:
        c.setFont("Courier-Bold" if label == "TOTAL" else "Courier", 8.5); c.drawString(6 * mm, y, label); c.drawRightString(w - 6 * mm, y, amt); y -= 4.6 * mm
    c.setFont("Courier", 7.5); c.drawCentredString(w / 2, y - 6 * mm, "Thank you! (synthetic receipt)")

def memo(c, d):
    w, h = A4
    c.setFont("Helvetica-Bold", 14); c.drawString(20 * mm, h - 25 * mm, "ACME HR SDN BHD — INTERNAL MEMO")
    y = h - 38 * mm
    for label, value in [("To", "All staff"), ("From", "Facilities"), ("Date", "2026-10-01")]:
        c.setFont("Helvetica-Bold", 10.5); c.drawString(20 * mm, y, label + ":"); c.setFont("Helvetica", 10.5); c.drawString(40 * mm, y, value); y -= 6.5 * mm
    c.line(20 * mm, y, w - 20 * mm, y)
    c.setFont("Helvetica", 11); c.drawString(20 * mm, y - 12 * mm, "The office pantry will be closed for renovation from 6 to 8 October."); c.drawString(20 * mm, y - 18 * mm, "Please use the level 3 pantry.")
    foot(c, w)

def email(c, d):
    w, h = A4
    c.setFillColor(colors.HexColor("#f3f4f6")); c.rect(15 * mm, h - 45 * mm, w - 30 * mm, 25 * mm, fill=1, stroke=0); c.setFillColor(colors.black)
    c.setFont("Helvetica", 9.5)
    for i, line in enumerate(["From: Farah (synthetic)", "To: HR", "Subject: Townhall on Friday"]): c.drawString(18 * mm, h - 27 * mm - i * 6 * mm, line)
    t = c.beginText(18 * mm, h - 60 * mm); t.setFont("Helvetica", 11); t.setLeading(16)
    for line in ["Dear HR,", "", "I would like to confirm my attendance at the townhall on Friday. Also, can I bring",
                 "my laptop charger to the meeting room?", "", "Thanks,", "Farah (synthetic)"]: t.textLine(line)
    c.drawText(t); foot(c, w)

DOCS = {
    "01-mc": (mc_formal, A4, dict(clinic="KLINIK MESRA BANGSAR", addr="12 Jalan Contoh, Bangsar, KL", no="2026-1001", doctor="Dr. A. Example (MMC 00000, synthetic)",
              body=["This is to certify that Aisyah Rahman was examined today and is unfit for duty", "for 2 day(s) from 05/10/2026 to 06/10/2026."])),
    "02-mc": (mc_form, A5, dict(rows=[("Klinik", "Poliklinik Sentosa, Petaling Jaya"), ("Nama pesakit", "Daniel Lee Wei Ming"), ("Tarikh mula", "12 Oct 2026"), ("Tarikh tamat", "12 Oct 2026"), ("Bilangan hari", "1")])),
    "03-mc": (mc_short, A5, dict(clinic="Klinik Dr. Tan & Rakan", text="Patient Priya Nair is given medical leave of 3 days, Monday 28 September 2026 until Wednesday 30 September 2026 inclusive.", remarks="Remarks: rest and fluids.")),
    "04-mc": (mc_formal, A4, dict(clinic="KLINIK KESIHATAN AMPANG", addr="3 Jalan Contoh, Ampang, Selangor", no="2026-1004", doctor="Dr. A. Example (MMC 00000, synthetic)",
              body=["This is to certify that Muhammad Faiz Hakim was examined today and is unfit for duty", "for 2 day(s) from 02/11/2026 to 03/11/2026."])),
    "05-mc": (mc_form, A5, dict(rows=[("Klinik", "Klinik Famili Cheras"), ("Nama pesakit", "Chong Mei Ling"), ("Tarikh mula", "20 Oct 2026"), ("Tarikh tamat", "22 Oct 2026"), ("Bilangan hari", "3")])),
    "06-mc": (mc_short, A5, dict(clinic="Poliklinik Harmoni Shah Alam", text="Patient Siti Nurain Osman is given medical leave of 2 days, Friday 14 August 2026 until Saturday 15 August 2026 inclusive.", remarks="Remarks: rest and fluids.")),
    "07-mc": (mc_formal, A4, dict(clinic="KLINIK SERI DAMANSARA", addr="8 Jalan Contoh, Damansara, KL", no="2026-1007", doctor="Dr. A. Example (MMC 00000, synthetic)",
              body=["This is to certify that Ravi Subramaniam was examined today and is unfit for duty", "for 1 day(s) from 27/10/2026 to 27/10/2026."])),
    "08-mc": (mc_form, A5, dict(rows=[("Klinik", "Klinik Utama Puchong"), ("Nama pesakit", "Tan Jia Hui"), ("Tarikh mula", "07 Sep 2026"), ("Tarikh tamat", "09 Sep 2026"), ("Bilangan hari", "2")])),
    "09-mc": (mc_short, A5, dict(clinic="Poliklinik Bandar Baru", text="Patient Nurul Ain Kamal is given medical leave of 2 days, Tuesday 21 July 2026 until Wednesday 22 July 2026 inclusive.", remarks="Remarks: rest and fluids.")),
    "10-mc": (mc_formal, A4, dict(clinic="KLINIK MEDIK SUBANG", addr="5 Jalan Contoh, Subang Jaya, Selangor", no="2026-1010", doctor="Dr. A. Example (MMC 00000, synthetic)",
              body=["This is to certify that Lim Kok Seng was examined today and is unfit for duty", "for 0 day(s) from 30/10/2026 to 29/10/2026."])),
    "11-claim": (claim_form, A4, dict(no="EC-2026-0141", vendor="RideNow (synthetic)", date="03/10/2026", cur="MYR", items=[("Ride to client office", "28.50"), ("Ride back", "31.20")], sub="59.70", tax="0.00", total="59.70")),
    "12-claim": (receipt, None, dict(vendor="HOTEL SERI PANTAI PENANG", addr="Batu Ferringhi, Penang (synthetic)", date="08/10/2026", no="Claim no. EC-2026-0142", items=[("Room 2 nights", "MYR 420.00"), ("Breakfast", "MYR 60.00")], sub="MYR 480.00", taxlabel="Tax", tax="MYR 38.40", total="MYR 518.40")),
    "13-claim": (claim_form, A4, dict(no="EC-2026-0143", vendor="Kedai Buku Ilmu", date="11/10/2026", cur="MYR", items=[("Technical book", "129.90")], sub="129.90", tax="0.00", total="129.90")),
    "14-claim": (receipt, None, dict(vendor="RESTORAN SELERA KITA", addr="Jalan Contoh, Petaling Jaya (synthetic)", date="09/10/2026", no="Claim no. EC-2026-0144", items=[("Team lunch, 6 pax", "MYR 186.00")], sub="MYR 186.00", taxlabel="Tax", tax="MYR 11.16", total="MYR 197.16")),
    "15-claim": (claim_form, A4, dict(no="EC-2026-0145", vendor="SkyHop Air (synthetic)", date="25/09/2026", cur="MYR", items=[("KUL-PEN return", "289.00"), ("Baggage 20kg", "65.00")], sub="354.00", tax="0.00", total="354.00")),
    "16-claim": (receipt, None, dict(vendor="CLOUD TRAINING PTE LTD", addr="Singapore (synthetic)", date="18 October 2026", no="Claim no. EC-2026-0146", items=[("Online course", "SGD 250.00")], sub="SGD 250.00", taxlabel="Tax", tax="SGD 22.50", total="SGD 272.50")),
    "17-claim": (claim_form, A4, dict(no="EC-2026-0147", vendor="Parking Menara Contoh", date="21/10/2026", cur="MYR", items=[("Parking", "24.00")], sub="24.00", tax="1.44", total="26.44")),
    "18-claim": (receipt, None, dict(vendor="TECH MART PLAZA", addr="Jalan Contoh, KL (synthetic)", date="06/10/2026", no="Claim no. EC-2026-0148", items=[("USB-C adapter", "MYR 79.00"), ("HDMI cable", "MYR 35.00")], sub="MYR 114.00", taxlabel="Tax", tax="MYR 6.84", total="MYR 120.84")),
    "19-other": (memo, A4, {}),
    "20-other": (email, A4, {}),
}

def text_pdf(name, fn, size, data):
    buf = io.BytesIO(); c = canvas.Canvas(buf, pagesize=size or A4)
    c.setTitle(f"Synthetic training document {name}"); c.setAuthor("Anchor Academy (synthetic)")
    fn(c, data); c.showPage(); c.save(); return buf.getvalue()

def scanned(pdf_bytes, seed):
    """Image-only PDF that looks like a phone photo of a printed page: grey, a little turned, uneven light."""
    rnd = random.Random(seed)
    with tempfile.TemporaryDirectory() as tmp:
        src = os.path.join(tmp, "in.pdf"); open(src, "wb").write(pdf_bytes)
        subprocess.run(["pdftoppm", "-r", "130", "-png", "-singlefile", src, os.path.join(tmp, "p")], check=True)
        img = Image.open(os.path.join(tmp, "p.png")).convert("L")
    img = img.rotate(rnd.uniform(-2.2, 2.2), resample=Image.BICUBIC, expand=True, fillcolor=205)
    img = ImageOps.expand(img, border=30, fill=190)
    w, h = img.size
    shade = Image.linear_gradient("L").resize((w, h)).point(lambda v: 232 + v * 20 // 255)
    img = Image.composite(img, shade, img.point(lambda v: 255 - v))  # paper tint where the page is white
    noise = Image.effect_noise((w, h), 14).convert("L")
    img = Image.blend(img, noise, 0.08).filter(ImageFilter.GaussianBlur(0.6))
    img = ImageEnhance.Contrast(img).enhance(1.45)
    out = io.BytesIO(); img.convert("RGB").save(out, "PDF", resolution=130, quality=72); return out.getvalue()

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for i, (name, (fn, size, data)) in enumerate(DOCS.items()):
        pdf = text_pdf(name, fn, size, data)
        if name in SCANS: pdf = scanned(pdf, i)
        open(os.path.join(OUT, name + ".pdf"), "wb").write(pdf)
        print(f"{name}.pdf  {'scan' if name in SCANS else 'text'}  {len(pdf) // 1024} KB")
