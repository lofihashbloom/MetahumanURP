# Independent fiber angular data

These tools implement published circular-fiber optics, without Unreal renderer code. They bake intrinsic path-weighted angular densities so the strand fragment does not perform dozens of cylinder quadrature samples per light.

`generate_fiber_azimuth.py` requires Python3.9+ and NumPy. It writes `.mhfiber` data plus a JSON receipt. `validate_fiber_lookup.py` independently reads the half-float payload and checks symmetry and the declared common-longitudinal, zero-cuticle-shift furnace. Unity/GPU execution is a separate check.

```sh
python3 generate_fiber_azimuth.py --sigma-a 0 0 0 --name UnitAbsorption --status unit-validation --output UnitAbsorption.mhfiber
python3 generate_fiber_azimuth.py --pigments 8 0 --name DiagnosticDarkHumanHair --status diagnostic --output DiagnosticDarkHumanHair.mhfiber
python3 validate_fiber_lookup.py UnitAbsorption.mhfiber
```

The second example is a **diagnostic pigment profile, not a recovered Kellan material**. Current source metadata does not establish native final BaseColor or pigment concentrations. A source-input build must provide a source receipt:

```sh
python3 generate_fiber_azimuth.py --reflectance R G B --beta-n .37 --name DocumentedProfile --status source-input --source-receipt SourceColorReceipt.json --output DocumentedProfile.mhfiber
```

Reflectance mode explicitly uses the published Chiang2016 albedo-inversion approximation. Supplying native BaseColor does not prove that it has the same multiple-scattering-albedo semantics. Direct `--sigma-a` is in inverse physical fiber diameter; `--pigments cE cP` uses published RGB pigment cross-sections. Never pass normalized `hairMelanin` as a concentration by name alone. Keep inputs finite and physical: nonnegative absorption/concentrations, reflectance in(0,1], eta>1, betaN in(0,1].

## Parameterization and storage

- First data profile: eta1.55, betaN.37,512 relative-azimuth cells ×128 inclination-difference rows,64-point Gauss–Legendre cylinder integration.
- Relative azimuth spans[-pi,pi], periodic. Each texel contains the **cell-averaged** normalized logistic detector; this preserves angular mass across pixel bins rather than undersampling narrow peaks.
- Row thetaDifference spans[0,pi/2], including endpoints. Effective projected refraction uses `etaPrime=sqrt(eta²−sin²(thetaDifference))/cos(thetaDifference)`, with its grazing limit handled explicitly.
- Transmitted path length is `cos(gammaT)/cos(thetaT)` in units of physical diameter. The pixel-expanded raster ribbon width never enters absorption.
- Four linear RGBAHalf array slices: R,TT,TRT,all higher internal orders. RGB stores Fresnel/absorption-weighted angular density; alpha0 is **not opacity**. Higher orders use a declared uniform azimuth closure, not multiple-fiber scattering.
- `.mhfiber` header:8 ASCII bytes`MHFBR001`; four little-endian uint32 values(width,height,slices,JSON byte length); UTF8 metadata; slice-major/row-major RGBAHalf payload. No mipmaps, U repeat, V clamp, bilinear filtering.

Copy a generated file under a Unity `Assets` directory to invoke the standalone `HairFiberLookupImporter`; it creates a `HairFiberLookupTable` and linear `Texture2DArray` subasset. The importer/helper does not assign it to any groom material. `HairFiberLookup.hlsl` samples the table with correct endpoint-row texture coordinates. `MHHairFiberEvaluateCommonLookup` evaluates all four paths with one shared longitudinal density and optional explicit R surface-loss scale. Its output uses the cosine-weighted angular-response convention: the URP adapter applies pi once, with no additional ribbon NdotL.

The table is fixed in eta, azimuth roughness and absorption. Rebake when any of those, detector/closure definition, or numerical grid changes. The metadata and runtime `Matches` check make these dependencies explicit. This first table does not support per-strand pigment or azimuth-roughness variation. Longitudinal variance may vary per strand in the common-width configuration, but this must not be mislabeled as reproducing every native roughness variation.

## What the validation proves

Generator checks compare selected cells against an independent8192point midpoint cylinder integration, then test path masses before and after half quantization. Unit absorption has path-energy sum1 at each inclination; absorbing profiles remain bounded by1.

The separate furnace test combines serialized table masses with a direct double-precision Bessel longitudinal function. With a common longitudinal variance and zero cuticle shift, the supplied unit table has maximum furnace error4.6e−6 and exact serialized azimuth-reversal symmetry. A diagnostic unequal-width setting[v,v/4,4v,4v] loses up to23.7% in some white-furnace cases and exceeds energy by about0.5% elsewhere. That failure is retained in the report; no fitted brightness correction hides it. Do not claim those independently varied widths or nonzero cuticle shifts are validated by the common-width test.

The supplied artifacts are not imported/GPU-tested until the owning Unity workflow does so. A full native comparison must also hold coverage, source widths, tangent/Seed data, lights and camera fixed. Table mass conservation does not prove a match to Unreal's fiber shader, shadows, visibility or multiple-fiber scattering.

Primary sources: [Marschner etal.2003](https://cseweb.ucsd.edu/~henrik/papers/hair/), [d'Eon etal.2011](https://eugenedeon.com/pdfs/egsrhair.pdf), [Chiang etal.2016](https://media.disneyanimation.com/uploads/production/publication_asset/152/asset/eurographics2016Fur_Smaller.pdf). The project research and measured baseline are recorded in `Validation/GroomMatched/HairScatteringPlan-v10.md`.

# Hair-to-skin receiver shadow fields

`build_receiver_shadow_fields.py` (Python 3, NumPy) builds the static light-space optical-depth fields used by `HairReceiverShadowVolume`, one per light, from a GroomExportBridge `.mhgroom`. Light positions are given in the character root's Unity space (metres). Import the resulting `Fields.json` with **Assets > MetaHuman > Import Hair Receiver Shadow Fields...**. See [HairReceiverShadows.md](../../../Docs/HairReceiverShadows.md).

```sh
python3 build_receiver_shadow_fields.py --groom Hair.mhgroom --lights lights.json --output Fields
```
