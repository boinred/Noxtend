# 최신 3D Mesh 생성 AI 모델 조사

> 조사 기준일: 2026-08-11<br>
> 범위: 단일 오브젝트/캐릭터용 text-to-3D, image-to-3D, multi-view-to-3D 및 mesh 후처리<br>
> 출처 원칙: 공식 제품 문서·공식 블로그·공식 GitHub/Hugging Face·원 논문만 사용

## 결론

“최신”은 하나의 순위가 아니다. 현재 시장은 **완성형 상용 파이프라인**, **로컬 실행 가능한 공개 가중치**, **자연 장면 복원**, **깨끗한 토폴로지/파트 생성**으로 갈라져 있다.

- 웹/API에서 텍스트·이미지·멀티뷰부터 PBR, 리메시, 리깅까지 한 서비스로 묶으려면 **Meshy 6** 또는 **Tripo v3.1/P1.0**이 가장 완성된 선택이다. Meshy는 `meshy-t2` Smart Topology와 최대 8K 텍스처까지 제공하고, Tripo는 2M 폴리곤 H3와 2초대 구조화 메시 P1을 용도별로 나눈다. [Meshy API changelog](https://docs.meshy.ai/en/api/changelog), [Tripo API changelog](https://docs.tripo3d.ai/get-started/changelog.html)
- 최고 해상도와 표면 디테일을 우선하는 상용 후보는 **Hyper3D Rodin Gen-2.5**다. 다만 현재 공식 웹은 Gen-2.5를 표시하지만 공개 개발자 문서는 `tier=Gen-2`까지만 구체적으로 정의하므로, Gen-2.5 API 파라미터와 SLA는 계약 전에 별도 확인해야 한다. [Hyper3D image-to-3D](https://hyper3d.ai/features/image-to-3d), [Rodin Gen-2 API](https://developer.hyper3d.ai/api-specification/rodin-generation-gen2)
- 로컬 공개 가중치에서 **고해상도 + 복잡 토폴로지 + full PBR** 조합은 **TRELLIS.2**가 가장 강하다. 단일 이미지 입력, Linux/NVIDIA 24GB 이상이라는 제약이 있다. [TRELLIS.2 model card](https://huggingface.co/microsoft/TRELLIS.2-4B)
- 빠른 단일 이미지 프로토타입은 **SPAR3D**, 실제 자연 사진의 가림·잡동사니·다중 객체와 pose/layout 복원은 **SAM 3D Objects**가 특화되어 있다. 전자는 공식 발표 기준 0.7초, 후자는 기본 예제가 3D Gaussian PLY를 내므로 곧바로 게임용 PBR asset을 얻는 도구는 아니다. [SPAR3D 발표](https://stability.ai/news-updates/stable-point-aware-3d), [SAM 3D Objects GitHub](https://github.com/facebookresearch/sam-3d-objects)
- **Roblox Cube 3D v0.5**는 text-to-untextured-mesh 연구 모델이고, **CubePart**의 현재 공개 코드는 기존 메시를 의미 파트로 분해한다. 둘 다 저장소 라이선스가 research-only이므로 상용 게임 파이프라인에 바로 넣어서는 안 된다. [Cube GitHub](https://github.com/Roblox/cube), [Cube license](https://github.com/Roblox/cube/blob/main/LICENSE)
- Tencent의 최신 hosted 모델은 **HY-3D-3.1**, 실제 공개 가중치 PBR 파이프라인은 **Hunyuan3D 2.1**이다. 그러나 2.1 커뮤니티 라이선스는 대한민국을 Territory에서 명시적으로 제외한다. 한국에서 로컬 제품 후보로 채택하기 전에 Tencent의 별도 허가를 받아야 한다. [Tencent Cloud HY-3D-3.1](https://intl.cloud.tencent.com/document/product/1284/75540?lang=en), [Hunyuan3D 2.1 license](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE)

## 빠른 비교

| 계열 | 최신 확인 상태 | 입력 | 주 출력 | PBR/텍스처 | 리깅 | 이용 형태 |
| --- | --- | --- | --- | --- | --- | --- |
| Meshy | Meshy 6; `meshy-t2` 2026-07 | text, image, 1~4 images, chat | textured mesh; GLB/FBX/OBJ/STL/USDZ/3MF | base color + metallic/roughness/normal/emission, 2K/4K/8K | 별도 auto-rig/animation; API는 표준 humanoid 중심 | proprietary web/API/plugins/ComfyUI |
| Tripo | H3 v3.1, P1.0, Rig v2.5; 2026-03 | text, image, 2~4-view | triangle/quad/low-poly/part mesh | texture + PBR, UV, 최대 4K 옵션 | 별도 Rig v2.5 + retarget | proprietary web/API |
| Hyper3D Rodin | 웹 Gen-2.5; 공개 API 문서 Gen-2 | text, 1~5 images/multi-view | raw triangle/quad mesh; GLB/USDZ/FBX/OBJ/STL | PBR 또는 shaded, Gen-2 API 2K/4K | T/A pose만 문서화; skeleton/skin 근거 없음 | proprietary web/API/enterprise |
| Tencent Hunyuan | hosted HY-3D-3.1; open 2.1 | text/image/최대 8-view(hosted), image(open 2.1) | OBJ/GLB 또는 로컬 mesh | hosted texture; open 2.1 full PBR | 별도 Tencent Cloud auto-rig API | hosted API/web; 2.1 local weights |
| TRELLIS | 2025-03 text/training update | text, image, tuning-free multi-image | mesh, 3D Gaussian, radiance field | textured mesh; full PBR 명시는 v2가 우위 | 없음 | MIT local weights/code |
| TRELLIS.2 | 4B, 2025-12/2026-01 공개 | single image | PBR mesh/GLB | base color, roughness, metallic, opacity | 없음 | MIT local weights/code |
| SPAR3D | 2025-01 | single image; editable point cloud intermediate | UV-unwrapped textured GLB mesh | 논문상 PBR material | 없음 | local gated weights + preview API |
| Roblox Cube | v0.5 2025-07; CubePart 2026-05 | text+bbox / existing mesh+part names | OBJ / part별 GLB | 없음 | 없음 | research-only local weights |
| Meta SAM 3D Objects | 2025-11; weights updated 2026-06 | natural image + object mask | posed 3DGS PLY; mesh representation도 내부 제공 | texture/vertex color, explicit PBR map 근거 없음 | object rigging 없음 | gated local weights + Playground |

표의 Meshy 기능은 [Multi-Image API](https://docs.meshy.ai/en/api/multi-image-to-3d)와 [Rigging API](https://docs.meshy.ai/en/api/rigging), Tripo 기능은 [H3 multi-view API](https://docs.tripo3d.ai/model-generation/multiview-to-model-v3-0-v3-1.html)와 [Rig v2.5](https://docs.tripo3d.ai/zh/animation/rig-v2-5-20260210.html), Rodin 기능은 [Gen-2 API](https://developer.hyper3d.ai/api-specification/rodin-generation-gen2), Hunyuan 기능은 [Tencent Cloud 문서](https://cloud.tencent.com/document/product/1823/130082)를 기준으로 했다.

## 상용 API·서비스

### Meshy 6와 Smart Topology `meshy-t2`

Meshy 6는 2026-01-18 정식 발표됐고, 2026-01-19부터 Text/Image API의 `latest`가 Meshy 6를 가리키며 01-26에는 Multi-Image도 full Meshy 6로 전환됐다. 2026-07-13에는 image-to-3D의 `smart-topology` 기본 모델로 `meshy-t2`가 추가되어 깨끗한 토폴로지, native separated parts, 목표 face count를 제공한다. [Meshy 6 발표](https://www.meshy.ai/blog/meshy-6-launch), [API changelog](https://docs.meshy.ai/en/api/changelog)

- 입력/출력: text, 단일 image, 동일 객체 1~4 images, chat/agent 입력을 받고 GLB·FBX·OBJ·STL·USDZ·3MF를 내보낸다. triangle/quad, adaptive decimation, Smart Topology를 선택할 수 있다. [Meshy docs](https://docs.meshy.ai/en), [Multi-Image API](https://docs.meshy.ai/en/api/multi-image-to-3d)
- 재질: base color에 metallic·roughness·normal을 추가할 수 있고 Meshy 6에는 emission map도 포함된다. 2K/4K/8K를 지원하지만 8K에서는 emission과 quad가 제외된다. [Multi-Image API](https://docs.meshy.ai/en/api/multi-image-to-3d)
- 리깅: 웹은 humanoid/quadruped를 지원하지만 API 문서는 현재 textured standard humanoid만 안정 대상으로 명시하며, non-humanoid·불명확한 사지·300K 초과 face는 부적합하다. 즉 웹과 API의 지원 범위를 동일하다고 가정하면 안 된다. [Web Rigging](https://docs.meshy.ai/en/webapp/guides/3d-model/rigging), [Rigging API](https://docs.meshy.ai/en/api/rigging)
- 판단: 가장 넓은 end-to-end workflow가 장점이다. 단점은 proprietary cloud와 credit 비용, 그리고 “생성됨”이 모든 asset의 deformation-ready topology를 보증하지 않는다는 점이다. 캐릭터는 Smart Topology/remesh 후 rig 결과를 실제 동작으로 검수해야 한다. [API pricing](https://docs.meshy.ai/en/api/pricing)

### Tripo v3.1, P1.0, Rig v2.5

Tripo는 2026-03-11에 **H3 v3.1 (`v3.1-20260211`)**, **P1.0 (`P1-20260311`)**, **Rig v2.5**를 공개했다. H3 v3.1은 Standard 최대 1.5M, Ultra 최대 2M triangle을 지향하고, P1은 face limit 48~20K의 구조화 low-poly mesh와 약 2초 생성에 초점을 둔다. [Tripo changelog](https://docs.tripo3d.ai/get-started/changelog.html), [H3 multi-view](https://docs.tripo3d.ai/model-generation/multiview-to-model-v3-0-v3-1.html), [P1 text-to-model](https://docs.tripo3d.ai/model-generation/text-to-model-p1-20260311.html)

- 입력/출력: text, single image, front/left/back/right multi-view를 지원한다. H3는 triangle, quad FBX, smart low-poly, semantic parts를 지원하고 P1은 깨끗한 low-poly topology에 최적화됐다. [Tripo API introduction](https://docs.tripo3d.ai/get-started/introduction.html), [H3 multi-view](https://docs.tripo3d.ai/model-generation/multiview-to-model-v3-0-v3-1.html)
- 재질: texture와 PBR을 독립 설정하고 UV export, texture alignment, detailed/4K texture를 제공한다. 다만 `generate_parts=true`는 texture/PBR/quad와 호환되지 않으므로 “분리 파츠 + 완성 PBR”을 한 호출로 얻을 수 없다. [H3 multi-view](https://docs.tripo3d.ai/model-generation/multiview-to-model-v3-0-v3-1.html)
- 리깅: v2.5는 biped를 기본으로 하고 pre-rig check는 quadruped, hexapod, octopod, avian, serpentine, aquatic까지 분류한다. GLB/FBX로 rig하고 preset motion을 retarget할 수 있다. [Rig v2.5](https://docs.tripo3d.ai/zh/animation/rig-v2-5-20260210.html), [Pre-rig check](https://docs.tripo3d.ai/animation/pre-rig-check-v2-0-20250506.html)
- 판단: hero/high-poly는 H3, game-ready low-poly는 P1이라는 선택지가 분명하다. 반면 고급 옵션마다 credit가 추가되고, parts와 PBR 조합 제약 때문에 복합 asset pipeline은 여러 작업으로 나뉜다. [Tripo pricing](https://docs.tripo3d.ai/get-started/pricing.html)

공개된 **TripoSR**은 현재 Tripo 상용 v3.1의 오픈 버전이 아니라 2024년 Stability AI와 공동 개발한 별도 단일 이미지 reconstruction 모델이다. MIT code/weights와 0.5초 A100 결과는 유용하지만 최신 Tripo 품질을 대표하지 않는다. [TripoSR GitHub](https://github.com/VAST-AI-Research/TripoSR)

### Hyper3D Rodin Gen-2.5

공식 웹은 현재 Rodin Gen-2.5를 single image에서 10M+ polygon, 약 4초 geometry/약 5초 textured asset, PBR과 Smart Low-Poly를 제공하는 최신 모델로 소개한다. 그러나 공개 API 명세는 `tier=Gen-2`, 최대 5 images, Raw/Quad, 2K/HighPack 4K만 구체적으로 설명한다. **웹 최신 버전과 공개 API 계약 사이의 차이**가 가장 큰 조달 리스크다. [Hyper3D image-to-3D](https://hyper3d.ai/features/image-to-3d), [Rodin Gen-2 API](https://developer.hyper3d.ai/api-specification/rodin-generation-gen2)

- 입력/출력: text 또는 1~5 images를 받고 GLB·USDZ·FBX·OBJ·STL을 출력한다. 여러 이미지는 feature fuse 또는 동일 객체 multi-view concat으로 처리한다. [Rodin Gen-2 API](https://developer.hyper3d.ai/api-specification/rodin-generation-gen2)
- 재질: Gen-2 API의 PBR은 base color, metallicness, normal, roughness이고 Shaded와 All 모드도 있다. [Rodin Gen-2 API](https://developer.hyper3d.ai/api-specification/rodin-generation-gen2)
- 리깅: `TAPose`는 리깅하기 좋은 포즈를 만드는 옵션일 뿐 skeleton/skin output이 아니다. 공개 Rodin 생성 API에는 rigged output이 문서화되지 않았다. 따라서 별도 rigging 단계가 필요하다고 보는 것이 안전하다. [Rodin Gen-2 API](https://developer.hyper3d.ai/api-specification/rodin-generation-gen2)
- 판단: sculpt/high-poly, multi-image, quad/PBR 선택은 강점이다. proprietary service이고 Gen-2.5의 정확한 API parameter·비용·제한은 공개 문서만으로 확정할 수 없다는 점이 단점이다. API는 Business plan부터 제공된다. [Hyper3D pricing](https://hyper3d.ai/pricing?lang=en)

### Tencent Hunyuan3D hosted 3.1과 open 2.1

Tencent Cloud의 최신 production API는 `hy-3d-3.1`이다. text/image와 최대 8-view 입력, geometry/texture 품질 개선을 명시하며 OBJ/GLB 결과를 제공한다. 3.0은 sketch, low-poly, smart topology까지 지원하지만 3.1에서는 LowPoly/Sketch parameter가 빠진다. [Tencent TokenHub 3D](https://cloud.tencent.com/document/product/1823/130082), [International API](https://intl.cloud.tencent.com/document/product/1284/75540?lang=en)

Hunyuan3D 생성 모델 자체가 rigged mesh를 내는 것은 아니다. Tencent Cloud에는 캐릭터/동물 mesh에서 skeleton과 skinning을 만드는 별도 Auto Rigging 작업이 있으므로 필요하면 후처리로 조합해야 한다. [Tencent Cloud Auto Rigging API](https://proxy-hk.tencentcloud.com/document/product/1284/79641)

로컬 공개 계열의 기준선은 2025-06-13 공개된 Hunyuan3D 2.1이다. image-to-shape와 mesh+reference-image-to-PBR-paint를 분리하고 full weights와 training code를 제공한다. 공식 수치는 shape 10GB, texture 21GB, 전체 29GB VRAM이다. [Hunyuan3D 2.1 GitHub](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1), [원 논문](https://arxiv.org/abs/2506.15442)

중요한 제약은 라이선스다. 2.1의 `TENCENT HUNYUAN 3D 2.1 COMMUNITY LICENSE AGREEMENT`는 EU·영국·대한민국에 적용되지 않으며 Territory 밖 사용을 금지한다. 이는 단순한 attribution 문제가 아니므로 한국 법인/개발자의 사용은 별도 라이선스 확인 전 보류해야 한다. [공식 License](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE)

2026-08-03 발표된 **Hunyuan3D-Buffalo 1.0**은 생성·이해·자연어 mesh editing·semantic parts를 통합한 더 최신 연구지만 프로젝트 페이지가 아직 `Code (Stay Tuned)` 상태다. 현재 채택 가능한 모델로 분류하지 않는다. [Buffalo project](https://tencent-hunyuan.github.io/Hunyuan3D-Buffalo1.0/), [원 논문](https://arxiv.org/abs/2608.02711)

## 공개 가중치·로컬 실행

### Microsoft TRELLIS와 TRELLIS.2

초기 TRELLIS는 text/image를 받아 하나의 Structured LATent에서 mesh, 3D Gaussian, radiance field를 모두 만들며 GLB와 Gaussian PLY를 출력한다. 2025-03-25 text models와 training code가 공개됐지만 Microsoft는 image-conditioned model을 권장하고 text model은 데이터 한계로 창의성·세부 묘사가 약하다고 명시한다. MIT이고 Linux/NVIDIA 16GB 이상이 공식 기준이다. [TRELLIS GitHub](https://github.com/microsoft/TRELLIS)

TRELLIS.2-4B는 single image를 512³~1536³ O-Voxel로 생성해 PBR-ready GLB로 내보낸다. base color, roughness, metallic, opacity를 geometry와 함께 표현하고 open surface, non-manifold, enclosed internal structure를 다룬다. 공식 H100 시간은 512³ 약 3초, 1024³ 17초, 1536³ 60초이며 Linux와 NVIDIA 24GB 이상이 필요하다. [TRELLIS.2 GitHub](https://github.com/microsoft/TRELLIS.2), [model card](https://huggingface.co/microsoft/TRELLIS.2-4B), [Microsoft Research](https://www.microsoft.com/en-us/research/articles/trellis-2/)

TRELLIS.2에는 rigging이 없다. raw mesh에 작은 hole/topological discontinuity가 생길 수 있고 base model은 human preference alignment가 되지 않았다는 공식 limitation도 있다. 따라서 게임 적용 시 decimation, watertight/normal/UV 검수, LOD, collider와 rigging은 후단 작업이다. [TRELLIS.2 model card](https://huggingface.co/microsoft/TRELLIS.2-4B)

### Stability AI SPAR3D

SPAR3D는 단일 이미지에서 point diffusion으로 가려진 뒷면을 포함한 point cloud를 만든 뒤, image feature와 point cloud를 결합해 UV-unwrapped textured mesh/GLB를 복원한다. point를 삭제·복제·이동·추가·재색칠해 결과를 제어할 수 있고 none/triangle/quad remesh를 제공한다. 공식 발표 수치는 전체 0.7초, edited point cloud→mesh 0.3초다. [공식 발표](https://stability.ai/news-updates/stable-point-aware-3d), [GitHub](https://github.com/Stability-AI/stable-point-aware-3d), [원 논문](https://arxiv.org/abs/2501.04689)

기본 VRAM은 약 10.5GB, low-VRAM mode는 약 7GB이며 CUDA·CPU와 experimental Windows/MPS를 지원한다. code는 공개됐지만 weights는 Hugging Face 동의가 필요한 gated model이고 Stability AI Community License상 연매출 US$1M 이상 조직의 상업 사용은 Enterprise License가 필요하다. rigging은 제공하지 않는다. [GitHub](https://github.com/Stability-AI/stable-point-aware-3d), [model card](https://huggingface.co/stabilityai/stable-point-aware-3d)

### Roblox Cube 3D v0.5와 CubePart

Cube 3D v0.5는 text prompt와 optional XYZ bounding box에서 geometry-only OBJ를 생성한다. v0.5는 latent length를 512에서 1024로 늘리고 약 2.8M synthetic assets를 추가했지만, texture generation과 scene layout은 여전히 upcoming이다. 극단적인 bounding box에서는 disconnected component나 diagonal placement가 생길 수 있다고 공식 README가 밝힌다. [Cube GitHub](https://github.com/Roblox/cube), [v0.5 model card](https://huggingface.co/Roblox/cube3d-v0.5)

CubePart는 2026-05 발표된 open-vocabulary semantic part 연구다. 논문의 전체 시스템은 global prompt+part schema를 말하지만, **현재 공개 code/weights는 existing GLB mesh+part names를 받아 part별 GLB를 만드는 decomposition 모델**이다. watertight single surface와 +Y up/+Z forward 정렬을 선호하며 texture/PBR과 skeleton/skin은 만들지 않는다. [CubePart README](https://github.com/Roblox/cube/tree/main/cubepart), [원 논문](https://arxiv.org/abs/2605.28763)

두 모델의 저장소 `CUBE3D RESEARCH-ONLY RAIL-MS LICENSE`는 permitted purpose를 academic/research로 제한한다. Hugging Face의 “businesses of all sizes” 문구를 상용 사용 허가로 해석하지 말고, 저장소 라이선스를 기준으로 Roblox에 별도 확인해야 한다. [Cube license](https://github.com/Roblox/cube/blob/main/LICENSE), [v0.5 model card](https://huggingface.co/Roblox/cube3d-v0.5)

### Meta SAM 3D Objects

SAM 3D Objects는 2025-11-19 공개된 single-natural-image reconstruction 모델이다. image+object mask에서 object별 full shape, texture, scale/rotation/translation과 scene layout을 복원해 작거나 가려진 객체, clutter, unusual pose에 강하도록 설계됐다. single/multi-object를 지원하고 공식 quickstart의 기본 asset export는 `output["gs"].save_ply(...)`인 3D Gaussian splat이다. [Meta 발표](https://ai.meta.com/blog/sam-3d/), [GitHub](https://github.com/facebookresearch/sam-3d-objects)

모델 내부에는 mesh representation도 있지만 explicit metallic/roughness PBR map이나 object skeleton/skin을 제공한다는 공식 근거는 없다. 별도 **SAM 3D Body**는 human mesh recovery 모델이지 Objects 결과를 리깅하는 모델이 아니다. 공식 setup은 Linux x64, NVIDIA GPU 32GB 이상이며 checkpoints는 gated Hugging Face에서 SAM License 동의 후 받는다. [GitHub](https://github.com/facebookresearch/sam-3d-objects), [Setup](https://github.com/facebookresearch/sam-3d-objects/blob/main/doc/setup.md), [License](https://github.com/facebookresearch/sam-3d-objects/blob/main/LICENSE)

Meta가 밝힌 limitation은 moderate output resolution으로 복잡한 객체의 세부가 손실될 수 있고, 객체를 하나씩 예측해 contact/interpenetration을 공동 추론하지 못한다는 점이다. 따라서 자연 장면에서 객체와 pose를 추출하는 앞단에는 강하지만, game-ready PBR asset 생성기의 직접 대체재로 보면 안 된다. [Meta 발표](https://ai.meta.com/blog/sam-3d/)

## Nextend 관점 권고

1. **제품 MVP/서버 API:** Meshy 6과 Tripo v3.1/P1을 동일 reference image 세트로 A/B 테스트한다. hero/high-poly와 low-poly/rig 결과를 분리 평가하고, Rodin은 Gen-2.5 API 계약이 공개 문서와 일치하는지 영업 확인 후 후보에 넣는다.
2. **자체 호스팅:** 24GB+ NVIDIA 환경이면 TRELLIS.2를 PBR base asset 생성 기준선으로 삼고, 낮은 VRAM·낮은 latency가 중요하면 SPAR3D를 비교한다.
3. **캐릭터:** 생성 모델의 T/A pose와 실제 rigged mesh를 구분한다. Meshy/Tripo의 별도 rig API 또는 전문 rigging 도구를 후단에 두고 skeleton hierarchy, skin weights, deformation, root motion을 별도 검증한다.
4. **파트 기반 게임 오브젝트:** 상용에서는 Meshy `meshy-t2` native separated parts 또는 Tripo part/segmentation을 우선 검토한다. CubePart는 연구·평가에만 사용한다.
5. **자연 사진/scene ingest:** SAM 3D Objects로 mask별 pose/layout과 3DGS를 얻은 뒤, 게임 asset이 필요하면 별도의 mesh/PBR 정제 pipeline을 둔다.
6. **법적 제외/보류:** Hunyuan3D 2.1은 대한민국 license grant 문제가 해결되기 전 제품 후보에서 제외하고, Cube 계열은 research-only를 유지한다.

실제 선정 benchmark는 같은 입력 세트에 대해 silhouette/hidden-side fidelity, watertightness, non-manifold·self-intersection, triangle/quad flow, UV seam, PBR relighting, target polycount 편차, part separation, rig deformation, 생성 시간·실패율·비용을 측정해야 한다. 공급사 showcase나 서로 다른 논문의 자체 지표만으로 순위를 정하지 않는다.
