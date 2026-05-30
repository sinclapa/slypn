<script setup lang="ts">
import { RouterLink } from 'vue-router'
import TulipIcon from '@/components/common/TulipIcon.vue'

const year = new Date().getFullYear()
</script>

<template>
  <footer class="mt-24 border-t border-slypn-100 bg-slypn-700 text-slypn-50">
    <div class="mx-auto grid max-w-6xl gap-10 px-6 py-12 md:grid-cols-3">
      <div>
        <div class="flex items-center gap-2 font-display text-xl font-extrabold">
          <TulipIcon class="h-7 w-7 text-tulip" />
          <span>SLYPN</span>
        </div>
        <p class="mt-3 text-sm text-slypn-100/85">
          South London Younger Parkinson&rsquo;s Network &mdash; a community for
          working-age people living with Parkinson&rsquo;s in South London.
        </p>
      </div>

      <div>
        <h2 class="font-display text-sm font-semibold uppercase tracking-widest text-tulip">
          Explore
        </h2>
        <ul class="mt-4 space-y-2 text-sm">
          <li><RouterLink to="/about" class="hover:text-tulip">About</RouterLink></li>
          <li><RouterLink to="/articles" class="hover:text-tulip">Articles</RouterLink></li>
          <li><RouterLink to="/events" class="hover:text-tulip">Events</RouterLink></li>
          <li><RouterLink to="/resources" class="hover:text-tulip">Resources</RouterLink></li>
          <li><RouterLink to="/newsletter" class="hover:text-tulip">Newsletter</RouterLink></li>
        </ul>
      </div>

      <div>
        <h2 class="font-display text-sm font-semibold uppercase tracking-widest text-tulip">
          Affiliated with
        </h2>
        <p class="mt-4 text-sm text-slypn-100/90">
          SLYPN is affiliated with
          <a
            href="https://www.parkinsons.org.uk/"
            class="underline underline-offset-4 hover:text-tulip"
            rel="noopener"
            target="_blank"
          >Parkinson&rsquo;s UK</a>,
          the UK&rsquo;s leading Parkinson&rsquo;s charity.
        </p>
        <p class="mt-4 text-xs text-slypn-100/70">
          For people diagnosed with Parkinson&rsquo;s and in immediate need of support,
          please contact the
          <a
            href="https://www.parkinsons.org.uk/information-and-support/helpline-and-local-advisers"
            class="underline underline-offset-4 hover:text-tulip"
            rel="noopener"
            target="_blank"
          >Parkinson&rsquo;s UK helpline</a>.
        </p>
      </div>
    </div>

    <div class="border-t border-slypn-600/60">
      <div class="mx-auto flex max-w-6xl flex-col items-start justify-between gap-3 px-6 py-5 text-xs text-slypn-100/70 sm:flex-row sm:items-center">
        <p>&copy; {{ year }} South London Younger Parkinson&rsquo;s Network.</p>
        <p>
          Built with care &mdash;
          <a
            href="https://github.com/sinclapa/slypn"
            class="underline underline-offset-2 hover:text-tulip"
            rel="noopener"
            target="_blank"
          >source on GitHub</a>.
        </p>
      </div>
    </div>
  </footer>
</template>
