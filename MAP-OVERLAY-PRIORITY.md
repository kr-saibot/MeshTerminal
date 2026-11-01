# Overlay priority

Each `<Place>` may contain an optional integer `<Priority>12345</Priority>`.
Higher values take precedence when overlay labels collide. Entries with an explicit priority precede entries without one (including negative values). Equal priorities retain the existing shape and input ordering. Shape outlines and fills are drawn from lower to higher priority so higher values appear on top. Node markers, position markers and the topmost temporary path layer retain their existing layer behavior.

Older XML files remain valid and do not gain empty Priority elements when saved. The bundled `german-cities.xml` uses the population from each Description as its Priority; descriptions remain unchanged. Entries without population data have no priority.
