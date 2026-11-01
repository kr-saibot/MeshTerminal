# Chess messages

Chess is available from **Chats > Chess** in a direct chat, with either MeshCore
or Meshtastic. Both players need a version of MeshTerminal supporting this format.
Click a piece and its destination, or use the arrow keys and Enter/Space. Each
message contains one move (or Black's invitation with the initial board). Undo
only affects an unsent move. **Resend last move** sends the unchanged last own
chess message again while waiting for the opponent, including an initial invitation.
Resent moves do not add a half-move or erase the last move's highlights.
The board stays open after sending or resending and continues to update on incoming
moves. **Piece display** offers Symbols, Letters (ASCII), Block figures and Blocks with spacing. Letters switches the board, captured pieces and notation to
letters: K=king, Q=queen, R=rook, B=bishop, N=knight, P=pawn. White uses uppercase,
Black lowercase. The preference is saved and also applies to displayed chess
symbols in chat. It does not change the chess message format or stored messages. New game asks for a color and replaces the game only
when the new starting message is sent. White sends its first move; Black sends
an unchanged starting board, leaving White to move.

The board checks legal moves, king safety, castling, en passant and promotions;
checkmate and stalemate are displayed. No engine opponent is provided.

## Wire format

`///CHESS:<32 variable-length piece entries><new-game byte><turn byte>:<last move>`

Every position byte is represented by two ASCII hex digits. Without promotions,
piece entries occupy 64 ASCII bytes; the two flag bytes occupy four ASCII bytes.
Together with the prefix and separator this is 78 bytes before UTF-8 move notation.

Bit 7 (`80` hex) marks a promoted pawn. Its position byte is immediately followed
by one additional hex digit: `1` = queen, `2` = rook, `3` = bishop, `4` = knight.
Thus a promoted piece occupies three hex characters and every other piece two.
For example `B81` is a promoted queen on h7 (square 56 = `38`, plus `80`),
`884` a promoted knight on h1, and `802` a captured promoted rook. The promotion
marker and type remain set on every subsequent message, including after capture.
The next piece follows immediately. A receiver reads exactly 32 piece entries
before reading the two flag bytes.

Piece IDs (1-based, encoded in this order):

| IDs | Pieces |
| --- | --- |
| 1–8 | White rook a1, knight b1, bishop c1, queen d1, king e1, bishop f1, knight g1, rook h1 |
| 9–16 | White pawns a2 through h2 |
| 17–24 | Black rook a8, knight b8, bishop c8, queen d8, king e8, bishop f8, knight g8, rook h8 |
| 25–32 | Black pawns a7 through h7 |

Squares are numbered by rank: a1=1, h1=8, a2=9, …, h8=64 (`40` hex).
`00` marks a captured piece. The new-game byte is `01` for the first message
of a new game and `00` thereafter. Turn is `00` for White or `01` for Black.
New messages use ASCII piece names independently of the board display option:
`White pawn e2-e4`, `Black pawn e5xd4`, `O-O`,
`White pawn a7-a8=White knight`. Older messages with figurine symbols remain readable.
A check adds `+`. An unchanged invitation uses `Start`.

A promoted pawn keeps its ID; its current type comes directly from its piece
entry, independently of notation or previous messages. Old messages without the
promotion extension remain readable using their notation and history.
Castling rights and en-passant rights are reconstructed from consecutive positions.
Therefore the game history should be retained. If no new-game boundary can be
found, the dialog offers a fresh initial position. History uses database
reception/insertion order, not the sender's clock. The sender of a new-game frame
with White still to move is Black; the sender of the first white move is White.

Incoming frames must have the exact position/flag structure, valid hex values,
no duplicate occupied squares, both kings and no control characters in their
notation. An updated game replaces any unsent move in an open board. Full
snapshots preserve piece locations even if an intermediate move is missing,
but cannot independently recover all history-dependent castling/en-passant rights.

## Linux symbol rendering

The ncurses renderer receives one BMP character per chess figure, without Unicode
variation selectors. Black pawns use the text pawn outline with black foreground
to avoid emoji width differences. This affects display only, not piece identity
or the wire format. If the terminal font lacks chess glyphs, use the ASCII option
or select a terminal font providing Unicode chess characters.

## Block figures

The third display mode uses only spaces and the fixed-width block characters
`▀`, `▄` and `█`. Each figure is 7 columns by 5 text rows (7 by 10 pixels).
Each board cell is 8 columns by 5 rows. With coordinates and border, the board
is 70 columns by 44 rows; the full dialog can expand to 114 by 50.
If a block display does not fit the terminal, a notice appears and a smaller display is selected. The window is centered again on each display change. Arrow-key navigation
keeps the current cell in view. White and Black use different foreground
colours and the same silhouettes. Captured figures use letters in this mode.
Switching modes preserves the current position and an unsent move.

The fourth mode, **Blocks with spacing**, keeps the same sprites but enlarges
every cell to 11 columns by 6 rows. The board becomes 94 by 52, with a blank
row and additional horizontal space separating figures. Both block modes
place action buttons vertically in the right sidebar. The sidebar can scroll
on short terminals. Rank numbers are two text rows below each cell's top edge.

## Complete example

An invitation from Black with the initial position, a new-game flag of `01`
and White to move (`00`):

```text
///CHESS:0102030405060708090A0B0C0D0E0F10393A3B3C3D3E3F4031323334353637380100:Start
```

This is a full position snapshot, not a move command. Receivers must parse the
32 piece entries before reading the flags, because promoted pawns add one hex
digit each. The readable suffix is informational; it is not the authoritative
source of promoted piece types. Resending an identical snapshot must not count
as another move. The last move's changed squares are brown; check, checkmate
and stalemate notices are yellow. Display preferences do not alter the protocol.
