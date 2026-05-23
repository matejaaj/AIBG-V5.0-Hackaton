"""Generic search framework for turn-based state expansion.

This is intentionally small and reusable: a SearchState describes one node in the
search tree, while Search handles the frontier, visited sets, and path
reconstruction. Pathfinding code can plug in game-specific state transitions.
"""
from __future__ import annotations

from abc import ABC, abstractmethod
from collections import deque


class SearchState(ABC):
    """Abstract node used by the generic search engine."""

    def __init__(self, board, parent=None, depth=0):
        self.board = board
        self.parent = parent
        self.depth = depth

    @abstractmethod
    def unique_hash(self):
        """Return a stable identity for the state."""

    @abstractmethod
    def is_final_state(self):
        """Return True when this state satisfies the search goal."""

    @abstractmethod
    def get_next_states(self):
        """Return an iterable of successor states."""


class Search(ABC):
    """Frontier/visited/parent manager.

    The engine mirrors the concept used in the other project: frontier and
    processed states are tracked by `unique_hash()`, and the final path is
    reconstructed by following `parent` links.
    """

    def __init__(self, board):
        self.board = board

    def search(self, initial_state):
        """Run the search and return (path, processed_list, frontier).

        `initial_state` may be a state class or a prebuilt state instance.
        """
        state = initial_state(self.board) if isinstance(initial_state, type) else initial_state
        frontier = deque([state])
        frontier_hashes = {state.unique_hash()}
        processed = deque()
        processed_hashes = set()

        while frontier:
            current = self.select_state(frontier)
            current_hash = current.unique_hash()
            frontier_hashes.discard(current_hash)
            processed.append(current)
            processed_hashes.add(current_hash)

            if current.is_final_state():
                return self.reconstruct_path(current), processed, frontier

            next_states = current.get_next_states()
            filtered = []
            for next_state in next_states:
                state_hash = next_state.unique_hash()
                if state_hash in processed_hashes or state_hash in frontier_hashes:
                    continue
                filtered.append(next_state)
            frontier.extend(filtered)
            frontier_hashes.update(state.unique_hash() for state in filtered)

        return None, processed, frontier

    @staticmethod
    def reconstruct_path(final_state):
        path = []
        current = final_state
        while current is not None:
            path.append(current)
            current = current.parent
        path.reverse()
        return path

    @abstractmethod
    def select_state(self, states):
        """Select the next state to process from the frontier."""


class BreadthFirstSearch(Search):
    def select_state(self, states):
        return states.popleft()
