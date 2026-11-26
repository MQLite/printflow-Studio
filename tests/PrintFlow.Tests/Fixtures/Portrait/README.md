# Portrait review fixtures

NASA supplied the original photograph of astronaut Eileen Collins. Scikit-image distributes the photograph in the public domain. [Image attribution](https://scikit-image.org/docs/stable/api/skimage.data.html#skimage.data.astronaut) · [Original 512 × 512 PNG](https://raw.githubusercontent.com/scikit-image/scikit-image/v0.25.2/skimage/data/astronaut.png)

- `reference.png` is a 328 × 328 enlargement of the original rectangle from (142, 31) to (306, 195).
- `preserved.png` places those exact pixels at (24, 36) in a new 540 × 400 archive card.
- `changed.png` uses the same card. A local warp compresses the visible row of teeth and relaxes the corners of the mouth. The eyes, backdrop, typography and frame retain their original pixels.
