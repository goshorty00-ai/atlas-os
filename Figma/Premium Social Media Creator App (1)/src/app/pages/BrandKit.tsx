import { useState } from "react";
import { Check, Copy, Droplets, Image as ImageIcon, LayoutTemplate, Palette, Plus, Type, WandSparkles } from "lucide-react";
import { useStudio } from "../state/studioStore";

export function BrandKit() {
  const {
    state: { brandKit, assets },
    updateBrandKit,
    addBrandColor,
    removeBrandColor,
    addBrandFont,
    removeBrandFont,
    addBrandGradient,
    removeBrandGradient,
  } = useStudio();

  const [copiedColor, setCopiedColor] = useState<string | null>(null);
  const [newColorName, setNewColorName] = useState("");
  const [newColorValue, setNewColorValue] = useState("#22d3ee");
  const [fontFamily, setFontFamily] = useState("");
  const [fontRole, setFontRole] = useState("");
  const [fontWeight, setFontWeight] = useState("600");
  const [gradientName, setGradientName] = useState("");
  const [gradientFrom, setGradientFrom] = useState("#0ea5e9");
  const [gradientTo, setGradientTo] = useState("#d946ef");
  const [ctaInput, setCtaInput] = useState("");
  const [layoutRuleInput, setLayoutRuleInput] = useState("");

  const logoAssets = assets.filter((asset) => asset.kind === "image" || asset.kind === "logo");

  async function copyToClipboard(color: string) {
    try {
      await navigator.clipboard.writeText(color);
      setCopiedColor(color);
      window.setTimeout(() => setCopiedColor(null), 1500);
    } catch {
      setCopiedColor(null);
    }
  }

  function addCtaStyle() {
    if (!ctaInput.trim()) return;
    updateBrandKit({ ctaStyles: [...brandKit.ctaStyles, ctaInput.trim()] });
    setCtaInput("");
  }

  function addLayoutPrinciple() {
    if (!layoutRuleInput.trim()) return;
    updateBrandKit({ layoutPrinciples: [...brandKit.layoutPrinciples, layoutRuleInput.trim()] });
    setLayoutRuleInput("");
  }

  return (
    <div className="h-full overflow-y-auto bg-[#07080c]">
      <div className="p-8 space-y-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Brand Kit</h1>
          <p className="text-gray-400">Define the visual, verbal, and layout system that drives Create Studio, Video Studio, Voice Studio, and ATLAS packets.</p>
        </div>

        <div className="grid grid-cols-[1.1fr_0.9fr] gap-6">
          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-8">
            <div className="flex items-start justify-between gap-6">
              <div>
                <h2 className="text-2xl font-semibold text-white mb-2">{brandKit.brandName || "Brand identity system"}</h2>
                <p className="text-gray-400 max-w-xl">Use this page to define reusable colors, fonts, gradients, CTA language, watermarking, and layout principles that propagate through the rest of the studio.</p>
              </div>
              <div className="grid grid-cols-2 gap-3 w-[420px]">
                <input value={brandKit.brandName} onChange={(event) => updateBrandKit({ brandName: event.target.value })} placeholder="Brand name" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={brandKit.industry} onChange={(event) => updateBrandKit({ industry: event.target.value })} placeholder="Industry" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={brandKit.tone} onChange={(event) => updateBrandKit({ tone: event.target.value })} placeholder="Tone" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={brandKit.audience} onChange={(event) => updateBrandKit({ audience: event.target.value })} placeholder="Audience" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
              </div>
            </div>
          </section>

          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center gap-2 mb-4">
              <WandSparkles className="w-5 h-5 text-cyan-300" />
              <h2 className="text-lg font-semibold text-white">Brand Rules</h2>
            </div>
            <div className="space-y-4">
              <textarea value={brandKit.voiceNotes.join("\n")} onChange={(event) => updateBrandKit({ voiceNotes: event.target.value.split("\n").map((line) => line.trim()).filter(Boolean) })} placeholder="Voice rules, forbidden language, and storytelling direction." className="w-full h-28 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white resize-none" />
              <label className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-sm text-gray-300 flex items-center justify-between">
                Enable watermark
                <input type="checkbox" checked={brandKit.watermarkEnabled} onChange={(event) => updateBrandKit({ watermarkEnabled: event.target.checked })} />
              </label>
              <input value={brandKit.watermarkText} onChange={(event) => updateBrandKit({ watermarkText: event.target.value })} placeholder="Watermark text or signature" className="w-full rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
            </div>
          </section>
        </div>

        <div className="grid grid-cols-2 gap-6">
          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-2">
                <Palette className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Color System</h2>
              </div>
              <div className="flex gap-2">
                <input value={newColorName} onChange={(event) => setNewColorName(event.target.value)} placeholder="Color name" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={newColorValue} onChange={(event) => setNewColorValue(event.target.value)} type="color" className="h-12 w-16 rounded-2xl border border-white/10 bg-[#0c1016]" />
                <button onClick={() => { if (newColorName.trim()) { addBrandColor(newColorName.trim(), newColorValue); setNewColorName(""); } }} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white flex items-center gap-2"><Plus className="w-4 h-4" />Add</button>
              </div>
            </div>
            <div className="grid grid-cols-4 gap-4">
              {brandKit.colors.map((colorItem) => (
                <div key={colorItem.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                  <div className="aspect-square rounded-2xl mb-3" style={{ backgroundColor: colorItem.value }} />
                  <div className="text-sm font-medium text-white mb-1">{colorItem.name}</div>
                  <div className="flex items-center justify-between gap-2 text-xs text-gray-400">
                    <span>{colorItem.value}</span>
                    <button onClick={() => void copyToClipboard(colorItem.value)} className="w-6 h-6 rounded-lg hover:bg-white/10 flex items-center justify-center">{copiedColor === colorItem.value ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}</button>
                  </div>
                  <button onClick={() => removeBrandColor(colorItem.id)} className="mt-3 text-xs text-red-300">Remove</button>
                </div>
              ))}
              {brandKit.colors.length === 0 && <div className="col-span-4 rounded-2xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">No brand colors configured yet.</div>}
            </div>
          </section>

          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-2">
                <Droplets className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Gradients</h2>
              </div>
              <div className="flex gap-2">
                <input value={gradientName} onChange={(event) => setGradientName(event.target.value)} placeholder="Gradient name" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={gradientFrom} onChange={(event) => setGradientFrom(event.target.value)} type="color" className="h-12 w-14 rounded-2xl border border-white/10 bg-[#0c1016]" />
                <input value={gradientTo} onChange={(event) => setGradientTo(event.target.value)} type="color" className="h-12 w-14 rounded-2xl border border-white/10 bg-[#0c1016]" />
                <button onClick={() => { if (gradientName.trim()) { addBrandGradient(gradientName.trim(), gradientFrom, gradientTo); setGradientName(""); } }} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white">Add</button>
              </div>
            </div>
            <div className="space-y-3">
              {brandKit.gradients.map((gradient) => (
                <div key={gradient.id} className="rounded-2xl border border-white/10 bg-black/20 p-4">
                  <div className="h-20 rounded-2xl mb-3" style={{ backgroundImage: `linear-gradient(135deg, ${gradient.from}, ${gradient.to})` }} />
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-sm font-medium text-white">{gradient.name}</div>
                      <div className="text-xs text-gray-400">{gradient.from} → {gradient.to}</div>
                    </div>
                    <button onClick={() => removeBrandGradient(gradient.id)} className="text-xs text-red-300">Remove</button>
                  </div>
                </div>
              ))}
              {brandKit.gradients.length === 0 && <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">No gradients configured yet.</div>}
            </div>
          </section>
        </div>

        <div className="grid grid-cols-2 gap-6">
          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-2">
                <Type className="w-5 h-5 text-cyan-300" />
                <h2 className="text-lg font-semibold text-white">Typography</h2>
              </div>
              <div className="flex gap-2">
                <input value={fontFamily} onChange={(event) => setFontFamily(event.target.value)} placeholder="Family" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={fontRole} onChange={(event) => setFontRole(event.target.value)} placeholder="Role" className="rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <input value={fontWeight} onChange={(event) => setFontWeight(event.target.value)} placeholder="Weight" className="w-24 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
                <button onClick={() => { if (fontFamily.trim() && fontRole.trim()) { addBrandFont(fontFamily.trim(), fontRole.trim(), fontWeight.trim() || "600"); setFontFamily(""); setFontRole(""); setFontWeight("600"); } }} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white">Add</button>
              </div>
            </div>
            <div className="space-y-4">
              {brandKit.fonts.map((font) => (
                <div key={font.id} className="rounded-2xl border border-white/10 bg-black/20 p-5">
                  <div className="text-3xl text-white mb-2" style={{ fontFamily: font.family, fontWeight: font.weight }}>Atlas builds premium social systems</div>
                  <div className="text-sm text-gray-400">{font.family} · {font.role} · {font.weight}</div>
                  <button onClick={() => removeBrandFont(font.id)} className="mt-3 text-xs text-red-300">Remove</button>
                </div>
              ))}
              {brandKit.fonts.length === 0 && <div className="rounded-2xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">No typography roles configured yet.</div>}
            </div>
          </section>

          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center gap-2 mb-4">
              <ImageIcon className="w-5 h-5 text-cyan-300" />
              <h2 className="text-lg font-semibold text-white">Logos & Marks</h2>
            </div>
            <div className="grid grid-cols-3 gap-4">
              {logoAssets.map((asset) => (
                <button key={asset.id} onClick={() => updateBrandKit({ logoAssetIds: brandKit.logoAssetIds.includes(asset.id) ? brandKit.logoAssetIds.filter((id) => id !== asset.id) : [...brandKit.logoAssetIds, asset.id] })} className={`rounded-2xl border overflow-hidden transition-all ${brandKit.logoAssetIds.includes(asset.id) ? "border-cyan-400/30 bg-cyan-500/10" : "border-white/10 bg-black/20"}`}>
                  <div className="aspect-square bg-[#0c1016] p-4 flex items-center justify-center">
                    {asset.dataUrl ? <img src={asset.dataUrl} alt={asset.name} className="max-w-full max-h-full object-contain" /> : <ImageIcon className="w-8 h-8 text-gray-500" />}
                  </div>
                  <div className="p-3 text-left">
                    <div className="text-sm font-medium text-white truncate">{asset.name}</div>
                    <div className="text-xs text-gray-400 mt-1">{brandKit.logoAssetIds.includes(asset.id) ? "Selected" : asset.folder}</div>
                  </div>
                </button>
              ))}
              {logoAssets.length === 0 && <div className="col-span-3 rounded-2xl border border-dashed border-white/10 bg-black/20 p-6 text-sm text-gray-400">Upload real brand marks in Media Library first.</div>}
            </div>
          </section>
        </div>

        <div className="grid grid-cols-2 gap-6">
          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center gap-2 mb-4">
              <WandSparkles className="w-5 h-5 text-cyan-300" />
              <h2 className="text-lg font-semibold text-white">CTA Styles</h2>
            </div>
            <div className="flex gap-2 mb-4">
              <input value={ctaInput} onChange={(event) => setCtaInput(event.target.value)} placeholder="Add CTA phrasing or button style" className="flex-1 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
              <button onClick={addCtaStyle} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white">Add</button>
            </div>
            <div className="flex flex-wrap gap-2">
              {brandKit.ctaStyles.map((cta) => (
                <button key={cta} onClick={() => updateBrandKit({ ctaStyles: brandKit.ctaStyles.filter((item) => item !== cta) })} className="px-3 py-2 rounded-full border border-white/10 bg-white/5 text-sm text-gray-300">{cta}</button>
              ))}
              {brandKit.ctaStyles.length === 0 && <div className="text-sm text-gray-400">No CTA styles saved yet.</div>}
            </div>
          </section>

          <section className="rounded-[28px] border border-white/10 bg-gradient-to-br from-[#141822] to-[#0c1016] p-6">
            <div className="flex items-center gap-2 mb-4">
              <LayoutTemplate className="w-5 h-5 text-cyan-300" />
              <h2 className="text-lg font-semibold text-white">Layout Principles</h2>
            </div>
            <div className="flex gap-2 mb-4">
              <input value={layoutRuleInput} onChange={(event) => setLayoutRuleInput(event.target.value)} placeholder="Spacing, hierarchy, alignment, or safe-area rule" className="flex-1 rounded-2xl border border-white/10 bg-[#0c1016] px-4 py-3 text-white" />
              <button onClick={addLayoutPrinciple} className="rounded-2xl bg-gradient-to-r from-cyan-500 to-blue-600 px-4 py-3 text-sm font-medium text-white">Add</button>
            </div>
            <div className="space-y-2">
              {brandKit.layoutPrinciples.map((rule) => (
                <button key={rule} onClick={() => updateBrandKit({ layoutPrinciples: brandKit.layoutPrinciples.filter((item) => item !== rule) })} className="w-full rounded-2xl border border-white/10 bg-black/20 px-4 py-3 text-left text-sm text-gray-300">{rule}</button>
              ))}
              {brandKit.layoutPrinciples.length === 0 && <div className="text-sm text-gray-400">No layout principles captured yet.</div>}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
